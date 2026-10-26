# Free Unity WebGL multiplayer: final workflow

Status: design, not yet built. Based on `reports/Free Unity WebGL multiplayer workflow.md`, re-checked on 2026-10-26.
Page fetches were blocked in the research environment, so all checks below are web-search summaries. "Unverified" means nobody has tested it.

## 1. What the re-check found

| Claim | Result |
|---|---|
| A WebGL build cannot host (browsers have no listening sockets); Unity Transport WebSockets on WebGL are client only | **Confirmed** (Unity Transport 2.5 manual, via search) |
| An https page needs `wss://` | **Confirmed** (same manual) |
| Unity's manual itself suggests a TLS-terminating reverse proxy | **Confirmed**, with the warning that it is "for advanced users" |
| Unity Transport only talks to Unity Transport; it is not a generic WebSocket library | **Confirmed.** A Node gateway may relay bytes unchanged, but cannot speak to a Unity client itself. |
| Reverse proxy in front of Netcode for GameObjects (NGO) works | **Not confirmed.** I found no working example. One forum poster reported little luck and switched to Relay. |
| Unity Transport accepts a URL path (`wss://host/room/<id>`) | **Not found. Treat as false until tested.** The report's path-routing design depends on this. |
| nginx closes idle WebSockets after 60 s; upstream pings reset the timer | **Confirmed** from the nginx WebSocket docs. The ping must come from the game server. |
| Let's Encrypt IP certificates: free, 160 h lifetime, generally available since 2026-01-15 | **Confirmed** |
| IP certificates validate only by HTTP-01 or TLS-ALPN-01 (so the IP must be publicly reachable) | **Probable.** Stated in a pre-launch report; not re-confirmed after general availability. |
| Caddy can obtain IP certificates | **Probable**, from a third-party write-up (profile `shortlived`). Check Caddy's docs. |
| Mirror: WebGL needs wss, SSL settings are obsolete, use a reverse proxy | **Confirmed** (Mirror docs) |
| NGO issue #4111 (WebGL-hosted session) | **Corrected.** It concerns a Unity Multiplayer Services session with the distributed-authority transport, not a self-hosted setup. It does not affect this design; the conclusion that a WebGL host is a bad plan stands. |

**Main correction to the earlier report:** the one-WSS-port design assumed path routing. Unity's WebSocket client probably cannot send a path, so routing must be validated first. Section 4 gives the fallbacks.

## 2. Architecture

```
Browser (Unity WebGL, static files from the same origin)
   |  https://HOST/            -> WebGL build
   |  https://HOST/api/...     -> lobby (REST)
   |  wss://HOST[:PORT]/...    -> game room
   v
[ Caddy or nginx ]  TLS ends here. One certificate. Only this is exposed.
   |             |
   v             v
[ Node lobby ]   [ Unity Linux dedicated-server processes, one per room ]
 127.0.0.1:3000   ws://127.0.0.1:7000..7099  (plain ws, localhost only)
```

Two parts:

- **Multiplayer part (Node.js):** lobby and matchmaker. Create room, list rooms, join by code, start a room process, track its port, kill it when empty.
- **Game part (Unity 6.3 LTS):** one project with two build targets, WebGL client and Linux dedicated server. Both use NGO with Unity Transport 2.x over WebSockets. Mirror with SimpleWebTransport is the fallback; it has documented reverse-proxy guidance.

### Host as client vs dedicated server

A WebGL client cannot be the network host, so both modes run the same server build:

| Mode | What it means |
|---|---|
| Dedicated server | The lobby spawns a room process. Nobody owns it. |
| "Host is a client" | The lobby spawns the same room process and gives the creating player **room-owner rights** (start, kick, settings). Authority stays on the server. |

This also removes host migration: if the owner leaves, the server picks a new owner.

## 3. Where each piece is used

| Stage | Where | What runs | TLS |
|---|---|---|---|
| A. Daily development | Your PC | Unity Editor as client and server, Node lobby, WebGL build via a local static server | None. Serve the page over `http://` and use `ws://`. This is not mixed content, but I did not confirm it with a source. Plain http is not a secure context. |
| B. Integration test | VirtualBox Ubuntu VM, **bridged** adapter | Caddy or nginx, Node lobby, Linux server build, WebGL files, all under systemd | Caddy `tls internal` (install its root CA on each test device), or DuckDNS name with Caddy DNS-01 for browser-trusted certs |
| C. Public playtest | Oracle Cloud Always Free Ampere VM | Same as B | DuckDNS with Caddy DNS-01, or a Let's Encrypt IP certificate (needs a public IP) |

Use A for gameplay, B for anything involving TLS, proxy, ports or process spawning, C only for outside testers. Avoid sslip.io (shared rate limit), Cloudflare quick tunnels (no SLA, 200 request limit), and ngrok free (1 GB per month).

VirtualBox: with NAT, forward the proxy ports. A NAT or LAN VM is not publicly reachable, so Let's Encrypt IP and HTTP-01 certificates will not work there. Use DuckDNS DNS-01 or the local CA.

## 4. Routing rooms through one TLS endpoint

Do this spike first, on the VM, before building anything else. Try in this order and stop at the first that works.

1. **Path routing** (`wss://HOST/room/<id>`): works only if the Unity transport can send a path. Expected to fail for NGO; check the installed version.
2. **Proxy-terminated port range** (recommended fallback): the proxy listens on a public range, for example 8000-8099, holds the one certificate, and forwards each port to `127.0.0.1:7000+n`. This is your old one-port-per-session idea, but TLS lives in the proxy, so no per-port certificate and no TLS config in Unity. Cost: open the range in ufw, VirtualBox forwarding, and the router. Networks that only allow 443 will fail.
3. **Cookie routing on 443** (hacky, unverified): the page sets a cookie with the room id before the WebSocket opens, and a Node gateway reads it from the handshake and pipes frames to the room port.
4. **Different stack:** Colyseus (many rooms in one Node process, one port) if the game logic can live in Node, or Mirror with SimpleWebTransport. Colyseus's Unity SDK status for WebGL and version 0.17 is unconfirmed.

Whichever you pick, test one thing that was never verified: that a Node or proxy hop passes Unity Transport frames unchanged.

## 5. Build order

1. Unity 6.3 LTS project. Add NGO 2.x and Transport 2.x; pin versions in `Packages/packages-lock.json`.
2. Maze prototype in the Editor with two players, no lobby.
3. Linux Dedicated Server build: `-buildTarget Linux64 -standaloneBuildSubtarget Server`. Code guarded by `UNITY_SERVER`.
4. WebGL build connecting to the server over `ws://` on your PC (stage A).
5. Node lobby: create, list, join by code; spawns the server with `-batchmode -nographics -port P -logFile -`, waits for a ready signal, returns the connection details.
6. Routing spike (section 4) on the VM, then stage B.
7. Reaper: kill rooms that are empty past a grace period or stop sending heartbeats. Free ports on process exit. Kill orphans when the lobby starts. Cap concurrent rooms after measuring RAM per room on the VM (no figure found).
8. Reconnect: issue a rejoin token valid 30-60 s.
9. Optional: React lobby UI, GameCI builds. Skip both for the prototype. GameCI with a Personal licence is fragile.

## 6. Pitfalls to design for

- **Hidden tabs update about once per second** and run slow. The server needs to tolerate about a second of silence from a client; run game ticks on the server; reconnect and resync when the tab becomes visible. Test with visible side-by-side windows.
- **Idle timeouts:** have the game server send a ping every 30-60 s, or raise `proxy_read_timeout`.
- **WebGL build headers:** brotli or gzip files need the matching `Content-Encoding`; `.wasm` needs `application/wasm`. Decompression Fallback avoids header control but loads slower.
- **CORS:** serve the build and the API from the same origin and you avoid it.
- **Cache:** hash build filenames so browsers do not keep an old build.
- **Oracle Always Free** instances can be reclaimed if CPU, network and memory all stay under 20% for 7 days.
- **Licensing ceiling:** Unity Personal is free up to US$200,000 revenue or funding. Everything else listed is self-hosted and uncapped.
- **Multiplayer Play Mode** tests netcode but not real browser behaviour.

## 7. Still unverified

Path support in the Unity transport; Node or proxy passthrough of Unity frames; RAM per headless room; Caddy IP-certificate setup; Colyseus Unity SDK on WebGL; background-tab WebSocket survival. Decide on the final stack only after the section 4 spike.
