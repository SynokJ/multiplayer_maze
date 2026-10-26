# Session-based matchmaking / lobby architecture for self-hosted, free Unity WebGL multiplayer

Note on evidence: WebFetch of docs.unity3d.com and docs.colyseus.io failed (DNS), so findings rest on web-search result summaries. Several items are community posts, not official docs. Things I could not source are in Inferences or Gaps and are marked as such.

## 1. Can a WebGL player be the session host ("host as client")? How do relays / WebRTC fit?

### Takeaway
A browser WebGL build cannot listen for incoming connections, so with Unity Transport / NGO / Mirror over WebSockets the host must be a server process (dedicated Linux build, or a headless "host" build) and every WebGL player is a client. True browser-hosting needs WebRTC (or a relay), which is experimental or unsupported in the mainstream Unity stacks.

### Cited Findings
- Unity Transport documentation states that starting a server inside a WebGL player is currently impossible, even with the WebSocket interface, due to browser limits (no incoming sockets); attempting it throws. — [Unity forum: NetCode WebGL build can't connect to server or host](https://discussions.unity.com/t/netcode-webgl-build-cant-connect-to-server-or-host/874990) and [Unity Transport WebSockets manual](https://docs.unity3d.com/Packages/com.unity.transport@2.5/manual/websockets.html) (as relayed by search summary; page itself not fetched)
- Unity Transport's WebSocket support is not a general WebSocket library: it only connects UTP clients to UTP servers. — [Unity Transport WebSockets manual](https://docs.unity3d.com/Packages/com.unity.transport@2.5/manual/websockets.html) (via search summary)
- A WebGL page served over HTTPS cannot open plain `ws://`; it must use `wss://`, and one developer solved this with a proxy in front of the server. — [Unity forum thread](https://discussions.unity.com/t/netcode-webgl-build-cant-connect-to-server-or-host/874990)
- An open NGO issue (Distributed Authority topology) reports a WebGL build acting as session host listens but peers cannot connect; behavior differs by Editor vs WebGL host. Bug report, not a resolution. — [NGO issue #4111](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/issues/4111)
- Mirror's SimpleWebTransport is WebSocket-based and designed for WebGL (also works standalone); a headless Unity process on Linux can run as a Mirror "HOST" with WebGL builds connecting as clients. — [Unity forum: How can WebGL project be the host in webserver (vps)](https://discussions.unity.com/t/how-can-webgl-project-be-the-host-in-webserver-vps/921803) / [Mirror results via search](https://docsearch.algolia.com/mcp/docs/repo/mirrornetworking/mirror)
- Mirror's WebRTC transport is reported as experimental, only used on desktop, no relay fallback; a WebRTC proof of concept uses DataChannels in a full mesh that "probably won't scale past like 10 people". — [Unity forum: Multiplayer in browser games](https://discussions.unity.com/t/multiplayer-in-browser-games/925522), [musv2022](https://github.com/hiinaspace/musv2022)
- Unity's own Relay sample for Mirror is marked deprecated; Edgegap documents relays for Mirror P2P. — [Unity Relay for Mirror](https://docs.unity.com/relay/mirror), [Edgegap](https://edgegap.com/integration/add-relays-to-peer-to-peer-networking-games-in-unity-video-tutorial-(mirror-networking-netcode))
- Nakama "relayed match": all messages (including from a headless Unity client) pass through the server, which does not inspect data; relayed matches are in-memory and expire when the last player leaves. Authoritative matches run server logic (Go/TS/Lua) on a tick. — [Heroic Labs forum](https://forum.heroiclabs.com/t/relayed-match-connections-on-webgl/3139), [Nakama authoritative docs](https://heroiclabs.com/docs/nakama/concepts/multiplayer/authoritative/)

### Inferences
- The simplest "host is a player" model for WebGL: make one WebGL client the logical "host" (owner of authority in game rules) but have a dumb relay server (Node `ws` or Colyseus/Nakama relay) forward messages. This is a custom message layer, not NGO/Mirror's native host mode; NGO/Mirror would require a custom transport.
- With NGO/Mirror, the practical equivalent of "player-hosted" is: the server process runs NetworkManager as Host/Server headless and the first joiner is given "room owner" privileges at game level.
- Not sourced: WebRTC would also need STUN/TURN (free STUN exists; TURN relay costs bandwidth), another reason relays via WSS are simpler on one VM.

### Gaps
- No source found for a maintained, production-ready NGO/Unity Transport WebRTC transport for WebGL hosting.
- Could not verify NGO issue #4111 status.

## 2. Architecture pattern comparison: lobby + spawned servers, single WSS gateway, Colyseus/Nakama, one-port-per-session

### Takeaway
Best fit for free, no-domain, small VM: one public HTTPS/WSS entry (single port, e.g. 443) via a reverse proxy, a Node lobby that creates rooms and either spawns Unity server processes bound to localhost ports (proxied by room path) or, if Unity authority isn't required, uses Colyseus/Nakama rooms inside one process. One public port per session is workable but is the weakest option for firewalls, VirtualBox forwarding and certificates.

### Cited Findings
- Colyseus: WebSocket-based authoritative Node framework with Web, Unity, Defold, Haxe, Cocos clients; rooms are created on client request; by default matchmaking and rooms share one process/port (Unity SDK client example `ws://localhost:2567`). — [Colyseus Unity docs / overview](https://docs.colyseus.io/getting-started/unity), [GitNation](https://gitnation.com/contents/making-multiplayer-games-with-colyseus-nodejs-and-typescript)
- Colyseus can split a standalone matchmaker from separate game-server processes; each game server needs its own public address and a shared presence/driver such as Redis. After matchmaking the client's WebSocket must reach the instance owning the room (matters behind load balancers). — [Colyseus standalone matchmaker](https://docs.colyseus.io/matchmaker/standalone), [colyseus-vercel](https://github.com/endel/colyseus-vercel)
- Nakama docs: for complex server-side physics (e.g. Unity headless instances), Nakama can orchestrate matchmaking and result reporting while the Unity server does gameplay; forum advice is fast-paced authoritative games fit dedicated servers with Nakama for matchmaking/leaderboards/chat. Authoritative Nakama matches run until explicitly terminated unless configured. — [Heroic Labs docs](https://heroiclabs.com/docs/nakama/concepts/multiplayer/authoritative/), [Heroic Labs forum](https://forum.heroiclabs.com/t/keeping-matches-open-after-players-leave/3079)
- Nginx can proxy WebSocket (since 1.4) with path-based `location` blocks (e.g. /ws/chat, /ws/notifications) and path rewriting; upgrade headers must be forwarded; a `map` on `$http_upgrade` is recommended for mixed traffic. — [OneUptime nginx WebSocket guide](https://oneuptime.com/blog/post/2026-01-24-websocket-nginx-reverse-proxy/markdown), [LinuxCapable](https://linuxcapable.com/configure-nginx-websocket-proxy/)
- Caddy: a basic `reverse_proxy` block is usually enough for WebSockets; IP addresses can be site names with `tls internal`; Caddy's local CA root must be installed on clients (`caddy trust`). — [Caddy community: self-signed cert](https://caddy.community/t/how-to-use-self-signed-cert-with-reverse-proxy/25438), [Caddy IPv4 guide](https://oneuptime.com/blog/post/2026-03-20-caddy-reverse-proxy-https-ipv4/markdown)
- Edge case: one user with `tls internal` on an IP site and HTTPS upstream got 502 / no certificate for upstream IP (unresolved). — [Caddy community](https://caddy.community/t/v2-reverse-proxy-with-self-signed-node-fails-on-ip/8330)

### Inferences
- Pattern (a)+(b) combined (recommended): Node lobby (REST + WS) on 127.0.0.1; Unity Linux servers each listen plain `ws://127.0.0.1:<port>` on an internal range (e.g. 7800-7899); Caddy/nginx terminates TLS on one port and maps `/room/<id>` to the port. Because nginx locations are static, dynamic routing needs either (i) Node as the WS gateway that pipes the upgraded socket to the room port (the lobby reads the path/room code and proxies), or (ii) Caddy Admin API / nginx `map` + `auth_request`/`X-Accel`-style lookups. (Mechanism not sourced; design reasoning.) Path-prefix routing also requires the Unity client transport to support a path (UTP WebSocket "Path"/secure fields: verify in the package version used).
- One-port-per-session cons (reasoned, not sourced): each public port needs firewall rule and VirtualBox NAT/port-forward rule (forward the whole range upfront, but adds attack surface); TLS must be terminated per port or by proxy anyway; ports leak on crash, so a reaper is required; browsers behind corporate/school firewalls often only allow 443/80. Pros: simplest to code, no proxy routing logic, trivial isolation.
- Colyseus/Nakama avoid process-per-room memory cost (many rooms in one Node/Go process) but give up Unity-side simulation; fine for a lobby-authoritative or state-sync game (a maze game may fit).
- Process-per-room memory cost for Unity headless: not measured in any source found; test on the VM.

### Gaps
- No source found for experienced-developer recommendations specifically on Caddy/nginx dynamic room routing for Unity; the recommendation above is my synthesis.
- Could not fetch official Colyseus/Unity docs directly.
- No benchmark of headless Unity Linux server RAM per instance found.

## 3. Spawning, tracking, health-checking, and recycling Unity Linux server processes from Node

### Takeaway
No sources found specific to this; standard Node `child_process` practice applies. Treat the following as design guidance, not cited fact.

### Cited Findings
- Nginx's default 60s upstream read timeout will drop quiet WebSockets; app-level ping/pong every 30-60s or a raised `proxy_read_timeout` is the standard remedy. — [websocket.org nginx guide](https://websocket.org/guides/infrastructure/nginx/), [OneUptime](https://oneuptime.com/blog/post/2026-01-25-nginx-websocket-proxy/markdown)
- Nakama/Colyseus rooms illustrate the lifecycle that servers must implement themselves: an authoritative Nakama match persists until terminated. — [Heroic Labs forum](https://forum.heroiclabs.com/t/keeping-matches-open-after-players-leave/3079)

### Inferences (unsourced engineering guidance)
- Spawn: `spawn(binary, ['-batchmode','-nographics','-port',P,'-roomId',ID,'-logFile',path], {detached:false})`; allocate ports from a free-list (bind-test before use); keep a registry `{roomId, code, port, pid, state, players, lastActivity}`.
- Readiness: wait for child to print a ready line on stdout or for a TCP connect/HTTP health ping before returning the room URL.
- Health/recycle: child sends periodic heartbeat/player-count to lobby (HTTP POST on localhost) ; lobby kills with SIGTERM then SIGKILL after grace if no heartbeat or empty for N seconds; on `exit` event free the port and delete the room; on lobby start, kill orphans (pidfile or `pkill` by name) and use `prctl`/process-group so children die with parent; use systemd for the lobby.
- Empty shutdown: server calls `Application.Quit()` after grace period with 0 clients; lobby also enforces a hard TTL.

### Gaps
- No cited sources for Unity-specific headless args, Agones/Open Match concepts (searched out of budget); can mention they are Kubernetes-oriented and overkill for a single VM (unsourced).

## 4. Session lifecycle, authority, reconnect, host migration

### Takeaway
Not covered by sources found except general Nakama/Colyseus points; design below is inferred.

### Cited Findings
- Nakama relayed matches vanish when last player leaves; authoritative matches need explicit termination. — [Heroic Labs forum](https://forum.heroiclabs.com/t/keeping-matches-open-after-players-leave/3079)
- Colyseus rooms are created on demand by join/create requests. — [GitNation](https://gitnation.com/contents/making-multiplayer-games-with-colyseus-nodejs-and-typescript)

### Inferences
- States: created -> lobby (join by short code, ready flags) -> starting -> in-game -> ended -> reaped. Lobby REST: POST /rooms, GET /rooms, POST /rooms/join {code}; reconnect via a session token stored in client (rejoin window e.g. 30-60s).
- Host migration is avoided when the host is a server process; if a player is "owner", transfer ownership of lobby privileges only, authority stays on the server.

### Gaps
- No sources on host migration for Unity WebGL; no open-source WebGL lobby samples were identified in this pass.

## 5. Hidden issues: TLS without a purchased domain, CORS, idle timeouts, VM limits

### Takeaway
Biggest practical blocker is trusted TLS for a no-domain setup: WebGL pages and WSS need a certificate the browser trusts. Options reported: Let's Encrypt IP-address certificates (GA Jan 2026), free DuckDNS subdomain + ACME, or Caddy `tls internal` with the root CA installed on each client.

### Cited Findings
- Let's Encrypt announced general availability of certificates for IP addresses on 15 Jan 2026 (IPv4/IPv6), short-lived (~160 hours), free; they work only for public IPs, not private/local ones. — [Stackscale](https://www.stackscale.com/blog/lets-encrypt-opens-the-door-to-https-by-ip.md), [The Register](https://www.theregister.com/2025/07/03/lets_encrypt_rolls_out_free/), [DirectAdmin forum](https://forum.directadmin.com/threads/ssl-certs-for-ip-addresses-available-now.81730)
- Older guidance: use a free dynamic DNS name (DuckDNS, afraid.org) pointed at the IP to get a Let's Encrypt cert. — [techjunction](https://techjunction.co/?p=13716), [Home Assistant forum](https://community.home-assistant.io/t/duckdns-why-in-my-case/113278)
- Caddy `tls internal` issues certs from a local CA; root must be trusted on each client device. — [Caddy community](https://caddy.community/t/how-to-use-self-signed-cert-with-reverse-proxy/25438)
- Nginx default 60s proxy read timeout kills idle WebSockets; use 30-60s app pings and/or 3600s+ timeout; long timeouts leave zombie connections. — [websocket.org](https://websocket.org/guides/infrastructure/nginx/), [OneUptime](https://oneuptime.com/blog/post/2026-01-25-nginx-websocket-proxy/markdown)
- Mixed content: HTTPS pages need `wss://`. — [Unity forum](https://discussions.unity.com/t/netcode-webgl-build-cant-connect-to-server-or-host/874990)

### Inferences
- A VM in VirtualBox behind NAT is not a public IP: Let's Encrypt IP certs and DuckDNS HTTP-01 need the VM reachable from the internet on 80/443 (bridged adapter or router port forward); for LAN-only play use `tls internal` or a public-DNS-free approach with a trusted local CA. DuckDNS DNS-01 can issue certs for private IPs via DNS (unsourced).
- Serve the WebGL page and WSS from the same origin (same proxy) to avoid CORS entirely; otherwise the lobby REST needs CORS headers. (Reasoned.)
- Ping/pong in the Unity transport plus lobby heartbeat keeps proxies from closing sockets; I found no source confirming that ping frames reset nginx's timer in all setups.
- Small VM limits (RAM per headless Unity instance, CPU for physics) untested; plan to cap concurrent rooms.

### Gaps
- Did not verify Caddy support for Let's Encrypt IP-address certs, nor sslip.io/nip.io behavior with ACME rate limits.
- No source on VirtualBox NAT/bridged specifics.
