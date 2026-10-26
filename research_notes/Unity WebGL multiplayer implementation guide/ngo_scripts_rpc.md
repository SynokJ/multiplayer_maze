# NGO 2.x scripting for synced variables and RPCs (WebGL client + Linux dedicated server over WebSockets)

Method note. Docs hosts (docs.unity3d.com, mp-docs.dl.it.unity3d.com, discussions.unity.com) fail DNS via WebFetch in this environment. I instead sparse-cloned the NGO GitHub repo (branch develop-2.0.0, plus tag v2.13.3) and read the source directly. Claims marked [SRC] are read from that source. Claims marked [SEARCH] come only from WebSearch summaries (docs pages not read in full). The com.unity.transport package source is NOT on GitHub, so Transport-internal behaviour is [SEARCH] only. No code was compiled (no Unity editor here); every sample is "API names verified from source, not compiled - compile in Unity". Source base: https://github.com/Unity-Technologies/com.unity.netcode.gameobjects

## Versions and manifest.json

### Takeaway
Latest released tag seen is NGO v2.13.3 (package 2.13.3, min Unity 6000.0, depends on com.unity.transport 2.6.0). Add `com.unity.netcode.gameobjects` to manifest; Transport comes in as a dependency. Min Unity is 6000.0, so 6000.6.6f1 satisfies it on paper, but I could not verify 6000.6 specifically.

### Cited Findings
- v2.13.3 package.json: version 2.13.3, "unity": "6000.0", dependency "com.unity.transport": "2.6.0", dependency com.unity.nuget.mono-cecil 1.11.4 — [SRC package.json at tag v2.13.3](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/blob/v2.13.3/com.unity.netcode.gameobjects/package.json)
- develop-2.0.0 branch is already at 2.13.4 (Unreleased); CHANGELOG shows 2.13.3 dated 2026-09-14, 2.13.2 dated 2026-08-16 — [CHANGELOG](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/blob/develop-2.0.0/com.unity.netcode.gameobjects/CHANGELOG.md)
- Tags seen on remote: v2.9.0 ... v2.13.1, v2.13.2, v2.13.3 (git ls-remote); a develop-3.x.x branch exists (not released, ignore).
- Manifest lines (package ids): `"com.unity.netcode.gameobjects": "2.13.3"` and optionally pin `"com.unity.transport": "2.6.0"`. Collections/Burst come transitively.

### Inferences
- Pinning explicit versions is safer than relying on the Package Manager default; the Package Manager may offer a newer release than 2.13.3 by the time you install (not checked).

### Gaps
- No source confirming Unity 6000.6 specifically; 6000.0 is the declared minimum. Transport 2.6.0 is what NGO 2.13.3 requires; I did not check whether a newer Transport exists.

## RPC API (NGO 2.x)

### Takeaway
Use the unified `[Rpc(SendTo.X)]`. `[ServerRpc]`/`[ClientRpc]` still exist as thin subclasses of RpcAttribute (ServerRpc = SendTo.Server, ClientRpc = SendTo.NotServer) and are legacy, but `RequireOwnership` is obsolete in favour of `InvokePermission`. Method names must end in `Rpc` (ILPP compile error otherwise).

### Cited Findings
- `SendTo` members: Owner, NotOwner, Server, NotServer, Me, NotMe, Everyone, ClientsAndHost, Authority, NotAuthority, SpecifiedInParams (last one's doc: "cannot be sent without passing in a target in RpcSendParams") — [SRC RpcTarget.cs](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/blob/develop-2.0.0/com.unity.netcode.gameobjects/Runtime/Messaging/RpcTargets/RpcTarget.cs)
- Semantics: Server = server regardless of ownership (runs locally if invoked on server); NotServer = everyone but the server, filtered by observers, NOT sent to a host-mode server, runs locally if invoked on a client; Everyone = all, observer-filtered, runs locally; ClientsAndHost = same as Everyone in host mode, same as NotServer on dedicated server; Owner runs locally if caller is owner — same file
- `RpcAttribute(SendTo target)` fields: `Delivery` (RpcDelivery.Reliable default / Unreliable), `InvokePermission` (RpcInvokePermission.Everyone=0 / Server / Owner), `DeferLocal`, `AllowTargetOverride`, obsolete `RequireOwnership` — [SRC RpcAttributes.cs](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/blob/develop-2.0.0/com.unity.netcode.gameobjects/Runtime/Messaging/RpcAttributes.cs)
- Default InvokePermission is enum default 0 = Everyone (i.e. any client can invoke `[Rpc(SendTo.Server)]` on any NetworkObject unless you set `InvokePermission = RpcInvokePermission.Owner`). Obsolete message for ServerRpc says: use `[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]` or `...Owner` — same file. (Default of 0 inferred from enum ordering + field default not being set; verify in Unity.)
- Naming: ILPP errors "Rpc method must end with 'Rpc' suffix!", "ServerRpc method must end with 'ServerRpc' suffix!", "ClientRpc method must end with 'ClientRpc' suffix!" — [SRC NetworkBehaviourILPP.cs L1641-1655](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/blob/develop-2.0.0/com.unity.netcode.gameobjects/Editor/CodeGen/NetworkBehaviourILPP.cs)
- Params: `RpcParams` { RpcSendParams Send {BaseRpcTarget Target, LocalDeferMode}; RpcReceiveParams Receive {ulong SenderClientId} }; declare a trailing `RpcParams rpcParams = default` parameter and read `rpcParams.Receive.SenderClientId` — [SRC RpcParams.cs](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/blob/develop-2.0.0/com.unity.netcode.gameobjects/Runtime/Messaging/RpcParams.cs)
- Runtime targets: `RpcTarget.Single(clientId, RpcTargetUse.Temp)`, `Group(...)`, `Not(...)`, plus `RpcTarget.Server/NotServer/Everyone/...` fields, usable when attribute has `AllowTargetOverride = true` or `SendTo.SpecifiedInParams`. `RpcTargetUse` has Temp/Persistent (Persistent needs Dispose) — [SRC RpcTarget.cs](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/blob/develop-2.0.0/com.unity.netcode.gameobjects/Runtime/Messaging/RpcTargets/RpcTarget.cs)
- Docs (search summary only): Rpc attribute covers server->client, client->server, client->client; ClientRpc/ServerRpc described as legacy; RPC params docs at mp-docs.dl.it.unity3d.com/netcode/current/advanced-topics/message-system/rpc-params/ — [SEARCH result list](https://mp-docs.dl.it.unity3d.com/netcode/2.0.0/advanced-topics/message-system/rpc/)
- Class hosting an RPC must derive NetworkBehaviour and be on a spawned NetworkObject (standard NGO; class should be `partial`? not required in 2.x - ILPP weaves IL) — not verified in this pass.

### Inferences
- Supported parameter types (not verified in this pass, from NGO docs knowledge): C# unmanaged primitives, enums, Vector2/3/4, Quaternion, Color, Ray, FixedString*, arrays of those, INetworkSerializable structs/classes, string (allocates), NetworkObjectReference / NetworkBehaviourReference. Mark "unverified".
- For a dedicated server, use `SendTo.ClientsAndHost` or `SendTo.NotServer` for broadcast; `SendTo.Everyone` also works (the server executes it too).
- Always validate in server-side RPC: sender = `rpcParams.Receive.SenderClientId`; never trust a client-supplied id.

### Gaps
- Official supported-parameter list and INetworkSerializable rules not read (docs unreachable).

## NetworkVariable, NetworkList, spawning, lifecycle

### Takeaway
`NetworkVariable<T>(T value = default, NetworkVariableReadPermission readPerm = Everyone, NetworkVariableWritePermission writePerm = Server)`. T must be unmanaged/INetworkSerializable (or supported managed type); use FixedString32Bytes etc. for strings. `NetworkList<T>` needs unmanaged IEquatable T.

### Cited Findings
- NetworkVariable ctor signature, `OnValueChanged` delegate (previous, new), `Value` property — [SRC NetworkVariable.cs](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/blob/develop-2.0.0/com.unity.netcode.gameobjects/Runtime/NetworkVariable/NetworkVariable.cs)
- Defaults: Read Everyone, Write Server (`DefaultReadPerm`, `DefaultWritePerm`); read permissions: Everyone, Owner; write: Server, Owner — [SRC NetworkVariableBase.cs / NetworkVariablePermission.cs](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/blob/develop-2.0.0/com.unity.netcode.gameobjects/Runtime/NetworkVariable/NetworkVariableBase.cs)
- `NetworkList<T> where T : unmanaged, IEquatable<T>`, event `OnListChanged(NetworkListEvent<T>)`, ctor `NetworkList(IEnumerable<T> values = default, readPerm, writePerm)` — [SRC NetworkList.cs](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/blob/develop-2.0.0/com.unity.netcode.gameobjects/Runtime/NetworkVariable/Collections/NetworkList.cs)
- Lifecycle: `OnNetworkSpawn()`, `OnNetworkPostSpawn()` (protected), `OnNetworkDespawn()` on NetworkBehaviour — [SRC NetworkBehaviour.cs](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/blob/develop-2.0.0/com.unity.netcode.gameobjects/Runtime/Core/NetworkBehaviour.cs)
- NetworkObject: `Spawn(bool destroyWithScene=false)`, `SpawnWithOwnership(ulong clientId, ...)`, `SpawnAsPlayerObject(ulong clientId, ...)`, `Despawn(bool destroy=true)`, `ChangeOwnership(ulong)` — [SRC NetworkObject.cs](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/blob/develop-2.0.0/com.unity.netcode.gameobjects/Runtime/Core/NetworkObject.cs)
- `NetworkConfig.PlayerPrefab`, `NetworkConfig.Prefabs` (NetworkPrefabs with `NetworkPrefabsLists`), `TickRate` default 30, `AutoSpawnPlayerPrefabClientSide` default true, `ConnectionApproval` bool, `ConnectionData` byte[] — [SRC NetworkConfig.cs](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/blob/develop-2.0.0/com.unity.netcode.gameobjects/Runtime/Configuration/NetworkConfig.cs)
- Prefab registration: create asset via Create > Netcode > Network Prefabs List (class `NetworkPrefabsList : ScriptableObject`), add prefabs, assign list in NetworkManager inspector (Editor workflow; the player prefab also assigned in NetworkManager). Default prefab list is auto-created in the project (docs knowledge, unverified).

### Inferences
- Owner-write NetworkVariables are allowed in client-server mode (e.g. client-chosen name), but server-authoritative is safer; prefer RPC + server writing.
- Write NetworkVariables on the server in OnNetworkSpawn (guard `IsServer`); subscribe `OnValueChanged` in OnNetworkSpawn and unsubscribe in OnNetworkDespawn.

### Gaps
- Exact serialization rules for custom struct NetworkVariable (needs INetworkSerializable + IEquatable<T> for change detection) not read in source this pass.

## Transport (WebSockets / WSS) and the path question

### Takeaway
PATH QUESTION RESOLVED (high confidence on client side, medium on server side): NGO's `UnityTransport.ConnectionAddressData` has a `WebSocketPath` string (default "/"), passed to `settings.WithWebSocketParameters(path:)` when UseWebSockets is true and not Relay. Per Transport 2.2.0 changelog and 2.5 manual summaries, clients connect on that path and a server ACCEPTS CONNECTIONS ONLY ON THE CONFIGURED PATH. So the client CAN request a non-"/" path, but the Unity server will only accept the exact path it is configured with. Query strings are undocumented: treat as unsupported/unverified; use a path like `/room/abc123` (needs the server to be set to the same path, so it only works one room per server process, or terminate at a reverse proxy that rewrites the path to the server's configured path). Put the room token in ConnectionData (approval), not in the URL.

### Cited Findings
- `ConnectionAddressData` fields: Address, Port, `WebSocketPath` (tooltip: "Path to connect to or listen on when using WebSockets. Defaults to "/" if not set."), ServerListenAddress, ClientBindPort — [SRC UnityTransport.cs L215-260](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/blob/develop-2.0.0/com.unity.netcode.gameobjects/Runtime/Transports/UTP/UnityTransport.cs)
- Code: `if (m_UseWebSockets && m_ProtocolType != RelayUnityTransport && !string.IsNullOrWhiteSpace(ConnectionData.WebSocketPath)) settings.WithWebSocketParameters(path: ConnectionData.WebSocketPath);` — same file L544-548
- There is no dedicated `SetConnectionData` overload for the path; set it by assigning the struct: `utp.ConnectionData.WebSocketPath = "/x"` (ConnectionData is a public field) — same file L311; SetConnectionData overloads: `(string ipv4Address, ushort port, string listenAddress = null)`, `(bool forceOverrideCommandLineArgs, string, ushort, string = null)`, `(NetworkEndpoint, NetworkEndpoint listen = default)`. WARNING: SetConnectionData REASSIGNS `ConnectionData = new ConnectionAddressData{...}` (L929), which likely resets WebSocketPath to unset; so set the path AFTER SetConnectionData. (Reassignment seen; whether it copies WebSocketPath not fully read; order advice is safe either way.)
- Transport 2.2.0 (2023-12-01): "The WebSocket URL path can now be configured using NetworkSettings.WithWebSocketParameters." Manual 2.5: default "/"; "For clients, ... all connection requests go to the given path. On the server side, it will only allow connections made to the given path." — [SEARCH: Transport 2.5 websockets manual](https://docs.unity3d.com/Packages/com.unity.transport@2.5/manual/websockets.html), [Transport 2.6 changelog](https://docs.unity3d.com/Packages/com.unity.transport@2.6/changelog/CHANGELOG.html) (summaries only; pages not fetched)
- Docs also say the UTP WebSocket cannot talk to arbitrary WebSocket servers; both ends must be UTP — same SEARCH summary. A PlayFab issue shows a plain WS proxy fails if the server speaks UTP framing differently - [PlayFab issue](https://github.com/PlayFab/MultiplayerServerSecureWebsocket/issues/9) (search summary).
- Encryption fields in NGO UnityTransport: `bool UseEncryption`, `SetServerSecrets(string serverCertificate, string serverPrivateKey)` (PEM), `SetClientSecrets(string serverCommonName, string caCertificate = null)` (no CA => OS/browser store); server calls `settings.WithSecureServerParameters(cert,key)`, client `WithSecureClientParameters(commonName)` or with CA; throws if secrets not set before StartServer/StartClient — [SRC UnityTransport.cs L580-610, 1842-1880]
- `UseWebSockets` property (bool). On a non-Relay UNITY_SERVER build, WebSockets are allowed (the server-build error is only for Relay). On WebGL (non-Editor) `StartServer` throws "WebGL as a server is not supported by Unity Transport" — [SRC UnityTransport.cs L1886-1930]
- On WebGL, if UseWebSockets is false the transport logs a warning and uses WebSocketNetworkInterface anyway - same file.
- `-port` and `-ip` command line args are parsed by `SetConnectionData` unless `forceOverrideCommandLineArgs=true` — [SRC UnityTransport.cs L868-930]. In a WebGL client these args don't exist; for a dedicated server this lets `-port 7777` work for free but note it overrides your own parsed value unless you pass `true`.
- Unity guidance (search summary): without a certificate on the game server, use Relay or a reverse proxy doing TLS termination; Mirror docs and a forum user favour proxy termination — [SEARCH discussions](https://discussions.unity.com/t/issues-setting-up-encrypted-websocket-connection-with-unity-transport/1620104)

### Inferences
- Recommended architecture: browser connects `wss://host:443/<path>`; nginx/Caddy/Cloudflare terminates TLS and proxies as ws:// to the server process (`UseEncryption=false` on both client and server because the browser's WebGL transport uses the wss scheme how? UNVERIFIED: With UseEncryption=true the client uses wss; with false it uses ws, and an https page blocks ws as mixed content). So the WebGL client must set `UseEncryption = true` and `SetClientSecrets(serverCommonName: "yourhost")` (no CA), while the SERVER behind the proxy runs `UseEncryption=false`. This client/server asymmetry is plausible but NOT verified; confirm in Unity. Alternative: server terminates TLS itself with SetServerSecrets (PEM cert), which needs a real cert (Let's Encrypt) and port 443 or a per-room port.
- Port 443: client calls `SetConnectionData("game.example.com", 443)`. Per-room servers on different ports need a proxy that routes by path/subdomain to the right port; since the server only accepts one path, have the proxy rewrite the path to "/" (or the one configured path).
- Query string in WebSocketPath: unverified; avoid.

### Gaps
- Transport source (com.unity.transport) not on GitHub: cannot confirm server-side strict path matching or query handling from code; only manual summary.
- Whether `WithSecureClientParameters` on WebGL switches scheme to wss: not verified from source.

## Dedicated server startup, shutdown, WebGL constraints

### Takeaway
Use `#if UNITY_SERVER` / `Application.isBatchMode`, parse args via `System.Environment.GetCommandLineArgs()`, call `NetworkManager.Singleton.StartServer()`, `Shutdown()` then `Application.Quit()`. WebGL: client only.

### Cited Findings
- `NetworkManager.StartServer()`, `Shutdown(bool discardMessageQueue = false)`, `DisconnectClient(ulong clientId, string reason = null)`, `DisconnectReason`, `ShutdownInProgress`, `OnServerStarted`, `OnServerStopped(bool)`, `OnClientStarted`, `OnClientStopped(bool)`, `OnTransportFailure`, `OnClientConnectedCallback(ulong)`, `OnClientDisconnectCallback(ulong)`, `OnConnectionEvent(NetworkManager, ConnectionEventData)`, `ConnectedClientsIds`, `Singleton` — [SRC NetworkManager.cs](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/blob/develop-2.0.0/com.unity.netcode.gameobjects/Runtime/Core/NetworkManager.cs)
- Unity Transport docs: WebGL supports WebSocket client only — [prior notes](/home/user/multiplayer_maze/research_notes/Free%20Unity%20WebGL%20multiplayer%20workflow/netcode_options.md) (search summary) plus [SRC] throw in CreateDriver.
- Tick rate: `NetworkConfig.TickRate` default 30.

### Inferences (from general Unity knowledge, not verified in this pass)
- Server: `Application.targetFrameRate = 30..60` on dedicated server (headless otherwise spins) ; WebGL: set `Application.runInBackground = true` (Player Settings > Resolution > Run In Background) so networking continues in hidden tabs, but browsers still throttle timers/rAF in background tabs (~1 fps, heartbeat/timeouts risk); disconnect handling via `OnClientDisconnectCallback` and `NetworkManager.DisconnectReason`; reconnect = `Shutdown()` then wait for `ShutdownInProgress==false`, then `StartClient()` again with fresh ConnectionData (approval token). No threads in WebGL; UTP uses a JS WebSocket bridge. No host in WebGL.
- Multiplayer Services/Sessions/Relay are not needed: a custom Node lobby replaces matchmaking (allocates server process/port, issues a room token, returns address; Unity server validates token in ConnectionApproval).

### Gaps
- Exact tab-throttling thresholds and UTP heartbeat timeout settings (`HeartbeatTimeoutMS`, `DisconnectTimeoutMS`) not read; fields exist in UnityTransport but I did not list them. Recommend raising timeouts for backgrounded WebGL tabs.

## Connection approval

### Takeaway
Set `NetworkConfig.ConnectionApproval = true`, client sets `NetworkConfig.ConnectionData` (byte[]) before StartClient, server sets `ConnectionApprovalCallback`.

### Cited Findings
- `ConnectionApprovalRequest { byte[] Payload; ulong ClientNetworkId; }`, `ConnectionApprovalResponse { bool Approved; bool CreatePlayerObject; uint? PlayerPrefabHash; Vector3? Position; Quaternion? Rotation; bool Pending; string Reason; }`; callback type `Action<ConnectionApprovalRequest, ConnectionApprovalResponse>`; only one callback can be registered; clients do not invoke it; set it before StartServer; warns if callback set but ConnectionApproval disabled — [SRC NetworkManager.cs L749-760, 803-856, 1309-1325]
- `DisconnectClient(clientId, reason)` sends reason; client reads `NetworkManager.DisconnectReason`.

### Inferences
- If `Approved=false`, set `response.Reason`; client sees it in DisconnectReason.

### Gaps
- Default ConnectionApprovalTimeout field name not read.

## Code samples (all: API names verified from source; NOT compiled - compile in Unity)

### Sample 0: bootstrap (server + WebGL client + approval token)
Server bootstrap:
```csharp
#if UNITY_SERVER
using System;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class ServerBootstrap : MonoBehaviour
{
    string roomId, token;

    static string Arg(string name, string fallback)
    {
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return fallback;
    }

    void Start()
    {
        Application.targetFrameRate = 30;
        ushort port = ushort.Parse(Arg("-port", "7777"));
        roomId = Arg("-roomId", "dev");
        token  = Arg("-token", "");

        var nm  = NetworkManager.Singleton;
        var utp = (UnityTransport)nm.NetworkConfig.NetworkTransport;
        utp.UseWebSockets = true;
        utp.UseEncryption = false;                 // TLS terminated by reverse proxy
        // forceOverrideCommandLineArgs=true so UTP does not re-read -port/-ip
        utp.SetConnectionData(true, "0.0.0.0", port, "0.0.0.0");
        utp.ConnectionData.WebSocketPath = "/";    // set AFTER SetConnectionData

        nm.NetworkConfig.ConnectionApproval = true;
        nm.ConnectionApprovalCallback = Approve;
        nm.OnClientDisconnectCallback += id => Debug.Log($"left {id}");
        if (!nm.StartServer()) { Application.Quit(1); }
    }

    void Approve(NetworkManager.ConnectionApprovalRequest req, NetworkManager.ConnectionApprovalResponse res)
    {
        // payload "roomId|token|name"
        var parts = Encoding.UTF8.GetString(req.Payload).Split('|');
        bool ok = parts.Length >= 2 && parts[0] == roomId && parts[1] == token;
        res.Approved = ok;
        res.CreatePlayerObject = ok;    // uses NetworkConfig.PlayerPrefab
        res.Pending = false;
        if (!ok) res.Reason = "bad room token";
    }

    public void StopServer()
    {
        NetworkManager.Singleton.Shutdown();
        Application.Quit();
    }
}
#endif
```
Whether `Shutdown()` is complete before `Application.Quit()`: shutdown is processed on the next update when messages are queued; to be safe, hook `OnServerStopped` then call `Application.Quit()`.

WebGL client:
```csharp
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class ClientConnect : MonoBehaviour
{
    public void Join(string host, ushort port, string path, string roomId, string token, string playerName)
    {
        Application.runInBackground = true;
        var nm  = NetworkManager.Singleton;
        var utp = (UnityTransport)nm.NetworkConfig.NetworkTransport;
        utp.UseWebSockets = true;
        utp.UseEncryption = true;                       // wss (unverified mapping)
        utp.SetClientSecrets(host);                     // common name = hostname; null CA => browser store
        utp.SetConnectionData(host, port);
        utp.ConnectionData.WebSocketPath = path;        // after SetConnectionData

        nm.NetworkConfig.ConnectionApproval = true;
        nm.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes($"{roomId}|{token}|{playerName}");
        nm.OnClientDisconnectCallback += id => Debug.Log("disconnected: " + nm.DisconnectReason);
        nm.StartClient();
    }
}
```

### Sample 1: synced score / color / name (player prefab)
```csharp
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class PlayerState : NetworkBehaviour
{
    public NetworkVariable<int> Score = new NetworkVariable<int>(0);
    public NetworkVariable<Color> Tint = new NetworkVariable<Color>(Color.white);
    public NetworkVariable<FixedString32Bytes> PlayerName =
        new NetworkVariable<FixedString32Bytes>("Player");

    public override void OnNetworkSpawn()
    {
        Score.OnValueChanged += OnScore;
        Tint.OnValueChanged  += (_, c) => GetComponent<Renderer>().material.color = c;
        GetComponent<Renderer>().material.color = Tint.Value;
        if (IsServer)
        {
            Tint.Value = Color.HSVToRGB(Random.value, 0.7f, 1f);
            PlayerName.Value = $"P{OwnerClientId}";
        }
    }
    public override void OnNetworkDespawn() { Score.OnValueChanged -= OnScore; }
    void OnScore(int prev, int next) => Debug.Log($"{PlayerName.Value} score {prev}->{next}");

    public void AddScore(int n) { if (IsServer) Score.Value += n; }

    [Rpc(SendTo.Server)]
    public void SetNameRpc(FixedString32Bytes n, RpcParams p = default)
    {
        if (p.Receive.SenderClientId == OwnerClientId) PlayerName.Value = n;
    }
}
```

### Sample 2: chat via RPC
```csharp
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class Chat : NetworkBehaviour    // on a scene NetworkObject or the player
{
    [Rpc(SendTo.Server)]
    public void SendChatRpc(FixedString128Bytes msg, RpcParams p = default)
    {
        if (msg.Length == 0) return;
        BroadcastChatRpc(p.Receive.SenderClientId, msg);
    }

    [Rpc(SendTo.ClientsAndHost)]   // dedicated server: acts like NotServer
    void BroadcastChatRpc(ulong from, FixedString128Bytes msg)
        => Debug.Log($"[{from}] {msg}");
}
```

### Sample 3: server-authoritative movement from input RPC
```csharp
using Unity.Netcode;
using UnityEngine;

public class MoverNet : NetworkBehaviour
{
    Vector2 input; public float speed = 4f;

    void Update()
    {
        if (IsOwner)
        {
            var v = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (v != input) { input = v; SendInputRpc(v); }
        }
        if (IsServer)
            transform.position += new Vector3(input.x, 0, input.y).normalized * speed * Time.deltaTime;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner, Delivery = RpcDelivery.Reliable)]
    void SendInputRpc(Vector2 v) => input = Vector2.ClampMagnitude(v, 1f);
}
// Add NetworkTransform (server-authoritative, default) to replicate position.
```

### Sample 4: ready-up / game state
```csharp
using Unity.Netcode;
using UnityEngine;

public enum GamePhase : byte { Lobby, Countdown, Playing, Ended }

public class GameFlow : NetworkBehaviour
{
    public NetworkVariable<GamePhase> Phase = new NetworkVariable<GamePhase>(GamePhase.Lobby);
    public NetworkList<ulong> ReadyIds;
    void Awake() { ReadyIds = new NetworkList<ulong>(); }   // init in Awake

    public override void OnNetworkSpawn()
    {
        Phase.OnValueChanged += (a, b) => Debug.Log($"phase {a}->{b}");
        if (IsServer) NetworkManager.OnClientDisconnectCallback += id => ReadyIds.Remove(id);
    }

    [Rpc(SendTo.Server)]
    public void SetReadyRpc(bool ready, RpcParams p = default)
    {
        var id = p.Receive.SenderClientId;
        if (Phase.Value != GamePhase.Lobby) return;
        if (ready && !ReadyIds.Contains(id)) ReadyIds.Add(id);
        else if (!ready) ReadyIds.Remove(id);
        if (ReadyIds.Count == NetworkManager.ConnectedClientsIds.Count && ReadyIds.Count >= 1)
            Phase.Value = GamePhase.Playing;
    }
}
```
GameFlow lives on a scene-placed NetworkObject (in-scene placed objects spawn automatically with the server).

### Sample 5: custom INetworkSerializable struct
```csharp
public struct ScoreEntry : INetworkSerializable, System.IEquatable<ScoreEntry>
{
    public ulong ClientId; public int Score;
    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    { s.SerializeValue(ref ClientId); s.SerializeValue(ref Score); }
    public bool Equals(ScoreEntry o) => ClientId == o.ClientId && Score == o.Score;
}
// NetworkVariable<ScoreEntry> or NetworkList<ScoreEntry> both fine.
```
(INetworkSerializable / BufferSerializer shape from NGO docs knowledge; not re-read in source this pass.)

### Inferences on samples
- `NetworkList` must be created in Awake/field initializer (not in OnNetworkSpawn). Server-only writes by default.

## Gaps summary
- No compile test; samples need Unity compile pass.
- Docs-only claims (parameter types, INetworkSerializable shape, server path strictness, WebGL wss scheme mapping) unverified from source.
