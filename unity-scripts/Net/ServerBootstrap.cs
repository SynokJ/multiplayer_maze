// Assets/Scripts/Net/ServerBootstrap.cs   (UNTESTED; compile in Unity)
using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.Networking;

[Serializable] public class JoinClaims { public string r; public string n; public string j; public long exp; }

// Exists in every build target, so the scene never shows a "missing script".
// The real work only compiles into the Linux Dedicated Server build (UNITY_SERVER).
public class ServerBootstrap : MonoBehaviour
{
#if !UNITY_SERVER
    void Awake() { Destroy(this); }
#else
    string roomId; byte[] secret; int maxPlayers = 8;
    readonly HashSet<string> usedJti = new HashSet<string>();
    string lobbyUrl, internalKey;

    static string Arg(string name, string fallback)
    {
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return fallback;
    }
    static byte[] B64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }

    void Start()
    {
        Application.targetFrameRate = 30;               // headless loops otherwise spin a core
        ushort port = ushort.Parse(Arg("-port", "7777"));
        roomId     = Arg("-roomId", "DEV00");
        maxPlayers = int.Parse(Arg("-maxPlayers", "8"));
        secret     = Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("ROOM_SECRET") ?? "dev-secret-dev-secret-dev-secret!");
        lobbyUrl   = Environment.GetEnvironmentVariable("LOBBY_URL");
        internalKey= Environment.GetEnvironmentVariable("LOBBY_INTERNAL_KEY");

        var nm  = NetworkManager.Singleton;
        var utp = (UnityTransport)nm.NetworkConfig.NetworkTransport;
        utp.UseWebSockets = true;
        utp.UseEncryption = false;                      // TLS is terminated by nginx
        // true = do not let UTP re-read -port/-ip itself; 127.0.0.1 keeps the room off the network
        utp.SetConnectionData(true, "127.0.0.1", port, "127.0.0.1");
        var cd = utp.ConnectionData; cd.WebSocketPath = "/"; utp.ConnectionData = cd;   // AFTER SetConnectionData

        nm.NetworkConfig.ConnectionApproval = true;
        nm.ConnectionApprovalCallback = Approve;
        nm.OnServerStarted += () => { Debug.Log("ROOM_READY " + roomId + " " + port); StartCoroutine(Heartbeat()); };
        nm.OnClientDisconnectCallback += id => Debug.Log("left " + id);
        if (!nm.StartServer()) { Debug.LogError("StartServer failed"); Application.Quit(1); }
    }

    void Approve(NetworkManager.ConnectionApprovalRequest req, NetworkManager.ConnectionApprovalResponse res)
    {
        res.Approved = false; res.CreatePlayerObject = true; res.Pending = false;
        try
        {
            var tok = Encoding.UTF8.GetString(req.Payload).Split('.');
            if (tok.Length != 2) { res.Reason = "bad token"; return; }
            using var h = new HMACSHA256(secret);
            var expect = h.ComputeHash(Encoding.UTF8.GetBytes(tok[0]));
            if (!CryptographicOperations.FixedTimeEquals(expect, B64UrlDecode(tok[1]))) { res.Reason = "bad token"; return; }
            var c = JsonUtility.FromJson<JoinClaims>(Encoding.UTF8.GetString(B64UrlDecode(tok[0])));
            if (c.r != roomId)                                   { res.Reason = "wrong room"; return; }
            if (c.exp < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) { res.Reason = "token expired"; return; }
            if (!usedJti.Add(c.j))                               { res.Reason = "token reused"; return; }
            if (NetworkManager.Singleton.ConnectedClientsIds.Count >= maxPlayers) { res.Reason = "room full"; return; }
            res.Approved = true;
        }
        catch (Exception e) { Debug.LogWarning("approval error: " + e.Message); res.Reason = "bad token"; }
    }

    IEnumerator Heartbeat()                                  // lobby reaps rooms that stop reporting
    {
        var wait = new WaitForSeconds(5f);
        while (NetworkManager.Singleton.IsServer)
        {
            if (!string.IsNullOrEmpty(lobbyUrl))
            {
                var body = "{\"players\":" + NetworkManager.Singleton.ConnectedClientsIds.Count + "}";
                using var www = new UnityWebRequest(lobbyUrl + "/internal/rooms/" + roomId + "/heartbeat", "POST")
                {
                    uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                    downloadHandler = new DownloadHandlerBuffer()
                };
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("x-internal-key", internalKey);
                yield return www.SendWebRequest();
            }
            yield return wait;
        }
    }

    void OnApplicationQuit() { if (NetworkManager.Singleton) NetworkManager.Singleton.Shutdown(); }
#endif
}
