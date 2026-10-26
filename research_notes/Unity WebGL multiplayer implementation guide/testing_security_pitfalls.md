# Testing, Troubleshooting, Security, Monitoring, CI and Pitfalls for Unity WebGL + Linux Dedicated Server

Method note: WebFetch failed with DNS errors (ENOTFOUND) for developer.chrome.com, cheatsheetseries.owasp.org and docs.unity3d.com. Everything below therefore comes from WebSearch result summaries only; no page was read in full. Items marked "general knowledge" are not backed by a fetched source and must be verified before being stated as fact in the guide. YAML, shell and config snippets in the Inferences sections are authored by me, untested, and drawn from standard practice.

## 1. Testing (Play Mode, browsers, bots, NGO tests, load, smoke tests)

### Takeaway
Multiplayer Play Mode (MPPM) covers local multi-client testing in the Editor (max 4 editor players), but a plain Node `ws` bot cannot join a Unity Transport server because UTP's WebSocket layer only talks to other UTP endpoints. Load and smoke tests should therefore target the HTTP/TLS/WebSocket-upgrade layer, plus real headless Unity clients if you need full-protocol load.

### Cited Findings
- MPPM caps editor-based players at four (main Editor plus three virtual players); version 2.0 of the manual repeats the cap and adds up to four locally built instances. Virtual players cannot edit GameObjects. Requires Unity 6+, Windows/macOS; 2.0 needs Editor 6000.3.0b4 or later. — [Unity MPPM 2.0 manual (via search)](https://docs.unity3d.com/Packages/com.unity.multiplayer.playmode@2.0/manual/index.html); [About MPPM](https://mp-docs.dl.it.unity3d.com/mppm/current/about/)
- Unity's docs say UTP WebSocket support "is not intended as a general-purpose WebSocket library" and "can only be used to connect clients and servers that are both using UTP", so it cannot connect to arbitrary WebSocket servers. (Quote from search summary of the Transport 2.x websockets manual.) — [Transport 2.3 WebSockets](https://docs.unity3d.com/Packages/com.unity.transport@2.3/manual/websockets.html); [Transport 2.5](https://docs.unity3d.com/Packages/com.unity.transport@2.5/manual/websockets.html)
- Since Transport 2.2.0 the WebSocket URL path is configurable via `NetworkSettings.WithWebSocketParameters`. — [Transport 2.2 release notes via search](https://mp-docs.dl.it.unity3d.com/transport/current/release-notes/transport-2-0-0/); [Unity forum: set path of UnityTransport WebSocket](https://discussions.unity.com/t/networkmanager-how-to-set-path-of-unitytransport-websocket-tcp/1594491)
- NGO test helpers: `NetcodeIntegrationTestHelpers` (static) and `NetcodeIntegrationTest` base class live in namespace `Unity.Netcode.TestHelpers.Runtime`; the base class has an overridable `NumberOfClients` and a `NetworkManagerInstatiationMode` enum (PerTest, AllTests, DoNotCreate). Source seen was the 1.2.0 copy. — [NetcodeIntegrationTest.cs 1.2.0 mirror](https://plastichub.unity.cn/unity-tech-cn/MetacityTemplate/raw/commit/720f3ddc-bab8-4e90-92bf-5cfac1c70369...Packages/com.unity.netcode.gameobjects@1.2.0/TestHelpers/Runtime/NetcodeIntegrationTest.cs)
- Import problem: users could not import `Unity.Netcode.TestHelpers.Runtime`; reported fix was adding the test helpers package to `testables` in the manifest and referencing it in the test assembly definition. — [Unity forum](https://discussions.unity.com/t/cant-import-unity-netcode-testhelpers-runtime/916801)
- A Unity staffer's suggested workaround for custom WebSocket drivers in NGO: custom `INetworkStreamDriverConstructor` creating a `NetworkDriver` with `WebSocketNetworkInterface`, assigned to `UnityTransport.s_DriverConstructor`. Another user reported `WebSocketNetworkInterface` "could not be found" with Transport 2.1. — [Unity forum](https://discussions.unity.com/t/networkmanager-how-to-set-path-of-unitytransport-websocket-tcp/1594491); [Unity forum](https://discussions.unity.com/t/seeking-advice-on-websockets-configuration-for-webgl-client-and-unity-ngo-server/1513460)
- Nginx WebSocket proxy pattern: `map $http_upgrade $connection_upgrade`, `proxy_http_version 1.1`, `Upgrade`/`Connection` headers, and a longer `proxy_read_timeout` because default idle timeout is 60s; timeouts measure time between reads, so pings reset them. — [websocket.org nginx guide](https://websocket.org/guides/infrastructure/nginx/); [OneUptime 2026 guide](https://oneuptime.com/blog/post/2026-01-25-nginx-websocket-proxy/view)

### Inferences
- A Node `ws` bot cannot play the game. It can (a) test the TLS + HTTP 101 upgrade path through nginx/Caddy (the proxy completes the upgrade before UTP sees it), and (b) hammer the connection layer, but it cannot send valid UTP/NGO frames without reimplementing UTP. For protocol-level bots, run headless Unity clients (`-batchmode -nographics`, Linux/Windows standalone, which use UDP or the same WebSocket transport) in scripts. State this explicitly in the guide. (If the lobby/room-allocation service is plain Node HTTP/WS, a `ws` bot CAN fully test that part.)
- Local test ladder: (1) MPPM with 1 server + clients in Editor; (2) Editor host + WebGL build in two browser windows; use separate Chrome profiles or one normal plus one incognito window so tabs are not throttled/deduplicated and storage (PlayerPrefs/IndexedDB) is not shared; (3) full path through nginx/Caddy with real TLS on the VM.
- Because WebGL clients in non-visible tabs get throttled (section 3), do multi-client manual tests in separate windows (side by side), not tabs behind one another.
- Smoke-test commands (general knowledge, authored by me, untested). Replace host:
  - TLS handshake and protocol: `openssl s_client -connect game.example.com:443 -servername game.example.com -tls1_2 </dev/null | openssl x509 -noout -dates -subject -issuer`; check `-tls1_3` works and `-tls1_1` fails.
  - Cert expiry: `echo | openssl s_client -connect HOST:443 -servername HOST 2>/dev/null | openssl x509 -noout -enddate`.
  - Upgrade through proxy: `curl -i -N --http1.1 -H "Connection: Upgrade" -H "Upgrade: websocket" -H "Sec-WebSocket-Version: 13" -H "Sec-WebSocket-Key: SGVsbG8sIHdvcmxkIQ==" -H "Origin: https://game.example.com" https://game.example.com/ws`. Expect `HTTP/1.1 101 Switching Protocols` (a 400/426 from the upstream is still proof the proxy forwarded it; 502/504 means upstream down or not listening; 404 means wrong path). Repeat with a bad `Origin` and expect rejection once the allow-list is in place.
  - `websocat wss://game.example.com/ws` for echo servers/lobby; against a UTP server it will connect but bytes are meaningless.
  - Static assets: `curl -sI https://game.example.com/Build/game.wasm.br` should show `content-type: application/wasm` and `content-encoding: br`.
- Load testing: for the lobby (Node), use `autocannon`/`k6`/`artillery` (free, general knowledge). For Unity room servers, the realistic limit is per-process CPU/RAM on a small VM, so test by launching N headless clients or by measuring connection-layer capacity with idle `ws` connections to the proxy (tests nginx fd limits, `worker_connections`, `LimitNOFILE`). Measure RAM per room process and cap concurrent rooms accordingly.
- NGO integration tests: derive from `NetcodeIntegrationTest`, override `NumberOfClients`, use `[UnityTest]` coroutines, run in Play Mode test assembly with test helpers in `testables`. Exact helper method names (e.g. client-creation and wait-for-condition helpers) were NOT verified; read the package source under `Packages/com.unity.netcode.gameobjects/TestHelpers/Runtime/` before writing the chapter.

### Gaps
- No fetched Unity page confirming current (NGO 2.x) `NetcodeIntegrationTest` API (`CreateNewClients`, `WaitForConditionOrTimeOut`, etc.); search found only a 1.2.0 mirror.
- No source for the UTP wire format; "cannot interoperate" rests on Unity's doc statement and absence of any Node client examples. I did not find any project showing a Node client speaking UTP.
- No verified 2026 load-testing guidance for NGO room servers.
- Package names: the helpers are sometimes cited as a separate "testhelpers.runtime" package in forum posts; whether it is still separate in NGO 2.x is unverified.

## 2. Browser debugging and symptom -> cause -> fix table

### Takeaway
Most WebGL connection failures come from mixed content (HTTPS page + `ws://`), bad/untrusted certs, wrong port/path, proxy missing Upgrade headers, or missing `Content-Encoding`/MIME on compressed Unity files.

### Cited Findings
- Mixed content: HTTPS page opening `ws://` is blocked with "The page was loaded over HTTPS, but attempted to connect to the insecure WebSocket endpoint... this endpoint must be available over WSS"; fix reported was TLS-terminating proxy. — [Unity forum: NGO on WebGL](https://discussions.unity.com/t/unity-netcode-for-gameobjects-on-webgl/936202)
- Relay + WSS: error "Invalid connection type: wss. Must be udp or dtls." was fixed by updating NGO to 1.6+ and Transport to 2.0.2, enabling "Use Web Sockets", and passing "wss" to `SetRelayServerData`. — [Unity forum](https://forum.unity.com/threads/failed-to-connect-to-server.1417572/)
- NGO issue tracker: WebGL build acting as host fails for Editor clients (NGO 2.13.1, Unity 6000.5.4f1, Distributed Authority); Editor host works with WebGL clients. Confirms WebGL cannot be a listening server (browsers cannot listen). — [NGO issue #4111](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/issues/4111)
- Handshake "400 Unexpected response code" cases in a non-Unity context were attributed to wrong port and invalid SSL certificates. — [Photon forum](https://forum.photonengine.com/discussion/comment/61121/)
- "Unable to parse Build/....framework.js.br/.gz: file is corrupt or compression was misconfigured" means the server is not sending `Content-Encoding: br` (or gzip) for the compressed file; also `.wasm` needs `Content-Type: application/wasm`. Fixes: set the headers, or rebuild with gzip/Disabled compression. One report says Brotli requires HTTPS (single report, unverified). — [Unity forum 247939](https://discussions.unity.com/t/unity-webgl-unable-to-parse-build/247939); [Unity forum 305236](https://discussions.unity.com/t/unity-export-webgl-error-unable-to-parse-build-webgame-framework-js-unityweb-the-file-is-corrupt-or-compression-was-misconfigured/305236); [nginx community thread](https://community.nginx.org/t/unity-webgl-drift-hunters-on-nginx-correct-headers-for-br-gz-coop-coep-getting-mime-decode-errors/6083)
- Nginx closes idle proxied connections after 60s by default (`proxy_read_timeout`). — [websocket.org](https://websocket.org/guides/infrastructure/nginx/)

### Inferences
Troubleshooting table (symptoms from sources above where cited; remaining rows are general knowledge, flag as such):

| Symptom (where seen) | Likely cause | Fix |
|---|---|---|
| Console: "Mixed Content ... insecure WebSocket endpoint ... must be available over WSS" | Page on https, transport using ws:// / port 7777 raw | Serve server behind TLS proxy; client uses wss on 443 (cited) |
| `WebSocket connection to 'wss://...' failed` with no detail | Cert untrusted/expired/hostname mismatch; DNS wrong; port closed; proxy down | `openssl s_client`, open https://host in a tab to see cert error, check ufw/Oracle security list, `systemctl status` (general knowledge; Photon thread supports cert/port) |
| DevTools WS tab shows 400 / 426 / 404 on handshake | Wrong path (UTP path setting), proxy not sending Upgrade headers, upstream rejecting Origin | Align path (`WithWebSocketParameters`), add map/Upgrade headers, check allow-list |
| 101 then closes after ~60 s idle | nginx `proxy_read_timeout` default | Raise timeout and/or send keep-alive traffic (cited) |
| "Invalid connection type: wss. Must be udp or dtls." | Old NGO/Transport with Relay | Update, enable Use Web Sockets (cited) |
| "Unable to parse Build/*.br/.gz ... compression misconfigured" | Missing `Content-Encoding` | Fix server headers or change Compression Format (cited) |
| Wasm "incorrect response MIME type" | `.wasm` not `application/wasm` | Add mime type (cited) |
| CORS error on fetch to lobby API | Lobby lacks `Access-Control-Allow-Origin` for game origin | Add CORS headers for the exact game origin (general knowledge); WebSocket connections themselves are not subject to CORS, they send `Origin` and the server must check it |
| Game works in Editor, not in WebGL | WebGL cannot use UDP; transport "Use Web Sockets" off; WebGL as host | Enable WebSocket on both; dedicated server must listen on WS (cited by #4111) |
| Old behaviour after redeploy | Browser/CDN cache of Build files | Cache-bust: Unity hashes optional (`Name files as hashes`), set `Cache-Control: no-cache` on index.html and loader (general knowledge) |

### Gaps
- Exact strings for NGO's disconnect reasons ("Failed to connect to server") were not verified against NGO 2.x source; the thread titled that way ([forum.unity.com/threads/failed-to-connect-to-server.1417572](https://forum.unity.com/threads/failed-to-connect-to-server.1417572/)) is Relay-specific.
- No fetched Chrome/Firefox docs on exact mixed-content or certificate error wording.
- No verified nginx `.br` serving recipe from nginx or Unity docs (only a community thread with an unfinished answer).

## 3. Background-tab throttling and reconnect behaviour

### Takeaway
Hidden tabs get timers clamped (about 1 Hz; Chrome 88+ can go to once per minute under "intensive throttling"), and `Application.runInBackground` does not defeat browser throttling; WebSocket sessions can time out in hidden tabs, so design heartbeats and reconnect logic server-side-tolerant.

### Cited Findings
- Chrome 88 intensive throttling applies when: page hidden more than 5 minutes, chain count 5 or greater, page silent for at least 30 s, and WebRTC not in use; then timers in that group are checked once per minute. — [Chrome blog, timer throttling in Chrome 88](https://developer.chrome.com/blog/timer-throttling-in-chrome-88/) (conditions as quoted in search summary; page not read)
- Solace and XWiki reports: WebSocket clients disconnecting after ~5-6 minutes in a background Chrome tab when the server inactivity timeout was shorter than 60 s; suggested keep-alive/timeouts above 60 s. — [Solace community](https://community.solace.com/t/client-websocket-disconnects-when-browser-is-minimized-or-not-in-focus-chrome/690); [XWiki](https://jira.xwiki.org/browse/XWIKI-23928)
- Chrome 57 (2017) exempted tabs playing audio or with active WebSocket/WebRTC from budget throttling per press coverage; whether this still holds is unconfirmed. — [Android Police](https://www.androidpolice.com/2017/03/14/google-introduces-background-tab-throttling-chrome-57-desktop/); [Hongkiat](https://www.hongkiat.com/blog/chrome57-background-tab-throttling/)
- Unity manual: Run in Background keeps content running after focus loss, but browsers can still throttle; hidden tab content updates about once per second, slowing `Time.time` as `Time.maximumDeltaTime` is lower than 1 s. — [Unity WebGL performance (via search)](https://docs.unity.cn/Documentation/Manual/webgl-performance.html)
- Forum reports: `runInBackground = true` does not stop throttling and a Photon WebSocket game disconnected on tab switch; a Relay connection failed after ~2 minutes in a background tab with "player timed out due to inactivity" (Unity bug UGSB-9, marked Won't Fix). — [Unity forum 664084](https://discussions.unity.com/t/webgl-websockets-irresponsive-when-window-not-active/664084); [Unity issue tracker](https://issuetracker.unity.com/issues/22707/webgl-client-encounters-connection-errors-when-player-tab-runs-in-the-background)
- Apple engineer (2014 mailing list): Chrome, Firefox and Safari all limit background rendering; Safari clamps timers more when the page has no network activity. Dated. — [WHATWG list](https://lists.whatwg.org/pipermail/whatwg-whatwg.org/2014-February/084348.html)
- Page Visibility API distinguishes hidden from merely unfocused (blur/focus). — [MDN Page Visibility](https://developer.mozilla.org/en-US/docs/Web/API/Page_Visibility_API)

### Inferences
- Design rules: server-side idle/heartbeat timeout of at least 90-120 s for WebGL clients (comfortably above Chrome's 60 s throttled timer) or heartbeat driven by the receive side; do not kick on missed client pings alone; treat the room slot as reserved for a grace period (e.g. 30-60 s) with a reconnect token; on `visibilitychange` back to visible, check connection state and reconnect/resync full state; show "paused" for hidden players. NGO's UTP has its own `Disconnect Timeout` / `Heartbeat Timeout` / `Connect Timeout` settings; confirm exact names in your Transport version (not verified).
- Turn-based or tolerant gameplay is more robust than twitch gameplay for backgrounded tabs.
- Intensive throttling's 5-min-hidden threshold plus the Unity-era reports suggest drops typically show after 2-6 minutes; test by hiding a tab for 10 min in each browser.

### Gaps
- No current (2025-2026) Firefox or Safari documentation on WebSocket/timer behaviour in hidden tabs was retrieved; sources are 2014-2017 or user reports. Recommend an empirical test table in the guide.
- Whether Chrome's WebSocket exemption from intensive throttling still applies today is unverified (the 2021 post lists "WebRTC" only, per the search summary, not WebSocket).
- Unity Transport timeout property names/defaults not verified.

## 4. Security hardening

### Takeaway
Do the standard Linux baseline (key-only SSH, ufw, fail2ban, unattended-upgrades, non-root, sandboxed systemd unit), put TLS and rate limiting at the proxy, validate Origin and tokens at the handshake, and treat every RPC as untrusted.

### Cited Findings
- OWASP: check the `Origin` header on every WebSocket handshake against an explicit allow-list (browsers set it and scripts cannot override it); this defends against cross-site WebSocket hijacking since cookies ride the handshake; example uses `verifyClient` in Node `ws`. — [OWASP WebSocket Security Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/WebSocket_Security_Cheat_Sheet.html)
- Node `ws` supports `maxPayload` to cap message size. — [Backslash CWE-1385](https://appsec.backslash.security/cwe/1385)
- Secondary guidance recommends per-connection and global rate limiting and message size limits (figures like 60 msgs/min and 32 KiB are from a third-party file, not OWASP). — [websocket.org security guide](https://websocket.org/guides/security/); [ShieldNet skill file](https://raw.githubusercontent.com/ShieldNet-360/secure-vibe/main/skills/websocket-security/SKILL.md)
- systemd sandboxing: `NoNewPrivileges` blocks privilege gain via execve; `ProtectSystem=strict` mounts the whole hierarchy read-only except /dev, /proc, /sys (add `ReadWritePaths`/`StateDirectory`); `PrivateTmp` isolates /tmp; also PrivateDevices, ProtectHome, kernel-protection options; `MemoryMax`/`TasksMax` limit resources; audit with `systemd-analyze security <unit>`. — [oneuptime 2026 hardening](https://oneuptime.com/blog/post/2026-03-02-how-to-configure-systemd-service-hardening-on-ubuntu/markdown); [iwd hardening commit](https://kernel.googlesource.com/pub/scm/network/wireless/iwd/+/6e44295fe2d928ba6cfeb43c08028d5af245d1d5); [fastdeploy docs](https://fastdeploy-ephes.readthedocs.io/en/latest/systemd-hardening/)
- Caddy rate limiting needs a third-party module (`xcaddy build --with github.com/mholt/caddy-ratelimit`), zones with key/events/window; README says unfinished/not official. — [mholt/caddy-ratelimit mirror](https://github.com/tylerjl/caddy-ratelimit)
- nginx: `limit_req_zone`/`limit_req` and `limit_conn_zone`/`limit_conn` keyed on `$binary_remote_addr` applied to the WebSocket location (single blog source). — [OneUptime](https://oneuptime.com/blog/post/2026-01-25-nginx-websocket-proxy/view)
- Let's Encrypt: 5 duplicate certificates per 7 days (exact same hostname set); renewals exempt from the per-registered-domain limit but not the duplicate limit; sources conflict on the per-registered-domain limit (20 vs 50) — check letsencrypt.org/docs/rate-limits. Test with the staging environment (general knowledge). — [LE community](https://community.letsencrypt.org/t/understanding-certificate-limits/140807); [mochahost KB](https://kb.mochahost.com/docs/lets-encrypt-limits)

### Inferences
(All below are general-knowledge recommendations, not cited; verify in the guide.)
- ufw: `ufw default deny incoming; ufw default allow outgoing; ufw allow 22/tcp (or limit 22/tcp); ufw allow 80/tcp; ufw allow 443/tcp; ufw enable`. Do not open room-server ports publicly if rooms are behind the proxy on 127.0.0.1. On Oracle, the VCN security list/NSG must also allow 80/443 and the image's iptables rules may block traffic independently of ufw.
- SSH: `PasswordAuthentication no`, `PermitRootLogin no`, `PubkeyAuthentication yes`; keep a second session open while testing (`sshd -t` before reload).
- fail2ban: enable `sshd` jail (default); optional nginx jails; `fail2ban-client status sshd`.
- unattended-upgrades: `apt install unattended-upgrades; dpkg-reconfigure -plow unattended-upgrades`; consider `Automatic-Reboot` off for a game host or scheduled off-hours.
- Run Unity server as dedicated user `maze` via systemd with `User=`, `NoNewPrivileges=yes`, `ProtectSystem=strict`, `ProtectHome=yes`, `PrivateTmp=yes`, `StateDirectory=maze`, `MemoryMax`, `LimitNOFILE`, `Restart=on-failure`. Unity player may need a writable HOME/cache (`~/.config/unity3d`): set `Environment=HOME=/var/lib/maze` and test; reduce options if it fails to start.
- TLS: use Mozilla SSL Configuration Generator "intermediate" profile for nginx (TLS 1.2+1.3, ECDHE+AESGCM/CHACHA20); Caddy defaults are already sane (TLS 1.2+, automatic HTTPS) and need no tuning. Profile contents not fetched; generate from ssl-config.mozilla.org when writing.
- Origin check: do it at the proxy too (nginx `map $http_origin $origin_ok` and return 403) as well as in the lobby; note UTP/NGO itself has no Origin check, so the proxy is the only place to enforce it for the Unity server. Note Origin can be forged by non-browser clients, so it protects users from hijacking, not the server from bots.
- Auth: lobby issues short-lived signed token (JWT/HMAC) bound to room id and player id; room server validates at connection approval (`NetworkManager.ConnectionApprovalCallback`) and rejects unknown/expired tokens; never send secrets in query string logs (nginx logs URLs) - use headers/first message or short expiry.
- Server authority: validate every ServerRpc (range, rate, state, ownership, cooldown, finite floats, array/string lengths); never trust client position/score; use server-side movement and collision; cap RPCs per second per client and disconnect offenders; limit connections per IP and rooms per user.
- DoS on small VM: nginx `limit_conn` (e.g. 10-20/IP), `client_max_body_size`, short `client_header_timeout`, cap rooms by RAM, `MemoryMax` per room unit, put Cloudflare free proxy in front only if WebSocket + TLS mode allow (not verified). Note CGNAT/school networks share IPs, so do not set per-IP limits too low.
- Log hygiene: avoid logging tokens, IPs only as needed (privacy), rotate, no stack traces to clients.
- DuckDNS: token is an account secret in update URLs; keep it in a root-only file/env, never in a repo or crontab visible to others, and rotate if leaked (general knowledge).

### Gaps
- OWASP cheat sheet and Mozilla generator pages could not be fetched; Mozilla intermediate cipher list and OWASP message-size/rate guidance not verified from primary source.
- No source verified for Caddy `reverse_proxy` WebSocket handling or Caddy default TLS versions (general knowledge only).
- No evidence retrieved on ufw/fail2ban/unattended-upgrades specifics for Oracle Ubuntu images.
- Cloudflare-free-proxy suitability for UTP WebSocket traffic not researched.

## 5. Monitoring and logging

### Takeaway
Use journald for service logs with a per-room unit or log prefix, a tiny `/healthz` endpoint, and optional Prometheus; I found no verified sources in this session, so this section is mostly authored guidance.

### Cited Findings
- `systemd-analyze security <unit>` scores a unit's sandboxing (supports audits). — [fastdeploy docs](https://fastdeploy-ephes.readthedocs.io/en/latest/systemd-hardening/)
- Nginx proxied WebSocket connections idle out at 60 s unless pings/timeouts are tuned (health/monitor probes should not assume long-lived connections). — [websocket.org](https://websocket.org/guides/infrastructure/nginx/)

### Inferences
(General knowledge, untested.)
- `journalctl -u maze-lobby -f`, `journalctl -u 'maze-room@*' --since "1 hour ago" -p warning`, `journalctl --disk-usage`, `journalctl --vacuum-size=200M`, persistent logs via `Storage=persistent` and `SystemMaxUse=` in `/etc/systemd/journald.conf`.
- Per-room logs: launch rooms as systemd template units `maze-room@<port>.service` or `systemd-run --unit=maze-room-<id>`; Unity server `-logFile -` writes to stdout (journal) -- verify that flag on Linux standalone/dedicated server builds; prefix log lines with room id.
- Health endpoint: lobby `GET /healthz` returns `{ok, rooms, uptime}`; test via `curl -fsS`; use free UptimeRobot-type external pinger (free tier not verified) to also keep Oracle VM above the idle threshold with real traffic.
- Log rotation for nginx via logrotate (installed by default on Ubuntu).
- Optional Prometheus: `prom-client` in Node lobby, `node_exporter`; for a hobby guide, keep optional.

### Gaps
- No searches performed on journald, logrotate, Unity `-logFile` or Prometheus; all content above is unsourced.

## 6. CI/CD (GitHub Actions, GameCI, deploy via rsync)

### Takeaway
Node lobby lint/test and rsync-over-SSH deploy are straightforward and free; GameCI Unity builds with a Personal license are currently unreliable, so treat them as optional and prefer building locally and uploading the WebGL build.

### Cited Findings
- GameCI docs still describe the Personal flow (manual .ulf activation; secrets UNITY_LICENSE, UNITY_EMAIL, UNITY_PASSWORD), but a Unity forum thread reports Unity "no longer supports manual activation of Personal licenses". — [GameCI activation docs](https://game.ci/docs/github/activation/); [Unity forum](https://discussions.unity.com/t/unity-no-longer-supports-manual-activation-of-personal-licenses/926760)
- GameCI cli PR #246 states Unity's manual activation page now refuses Personal seats and that a .ulf can no longer be obtained on a free seat; it proposes a "personal" strategy using the Unity licensing client with email/password. Merge/release status unconfirmed. — [game-ci/cli PR #246](https://github.com/game-ci/cli/pull/246)
- An unofficial hack (revealing the hidden Personal option in the serial page) and a puppeteer-based tool (game-ci/unity-license-activate, old) exist; both fragile. — [DEV Community](https://dev.to/ankursheel/workaround-for-unity-personal-license-manual-activation-not-supported-49j2); [unity-license-activate](https://github.com/game-ci/unity-license-activate)
- Unity 6.1 manual says primary activation is via Unity Hub. — [Unity 6000.1 licensing manual](https://docs.unity3d.com/6000.1/Documentation/Manual/ManagingYourUnityLicense.html)
- `webfactory/ssh-agent` loads the key and sets up known_hosts for github.com only; own server host key must be added separately (e.g. `ssh-keyscan`). `burnett01/rsync-deployments` (v9 recommended) supports SSH keys, custom switches and `hosts-*` commands for known_hosts. — [ssh-agent mirror README](https://github.com/shaunco/ssh-agent); [rsync-deployments](https://docsearch.algolia.com/mcp/docs/repo/burnett01/rsync-deployments)

### Inferences
Working workflow (authored by me, untested; adjust paths, secrets and host). Secrets needed: `DEPLOY_SSH_KEY` (private key of a dedicated deploy key restricted on the server), `DEPLOY_HOST_KEY` (output of `ssh-keyscan -t ed25519 HOST`, pinned, not scanned at runtime), `DEPLOY_HOST`, `DEPLOY_USER`. Pin actions to a major version or SHA; verify current majors.

```yaml
name: ci-deploy
on:
  push:
    branches: [main]
  pull_request:

permissions:
  contents: read

jobs:
  lobby:
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: lobby
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: 22
          cache: npm
          cache-dependency-path: lobby/package-lock.json
      - run: npm ci
      - run: npm run lint
      - run: npm test
      - uses: actions/upload-artifact@v4
        with:
          name: lobby-dist
          path: |
            lobby
            !lobby/node_modules
          retention-days: 7

  deploy:
    needs: lobby
    if: github.ref == 'refs/heads/main' && github.event_name == 'push'
    runs-on: ubuntu-latest
    environment: production
    steps:
      - uses: actions/checkout@v4
      - name: Install deploy key and pinned host key
        env:
          KEY: ${{ secrets.DEPLOY_SSH_KEY }}
          HOSTKEY: ${{ secrets.DEPLOY_HOST_KEY }}
          HOST: ${{ secrets.DEPLOY_HOST }}
        run: |
          install -m 700 -d ~/.ssh
          printf '%s\n' "$KEY" > ~/.ssh/deploy_key
          chmod 600 ~/.ssh/deploy_key
          printf '%s %s\n' "$HOST" "$HOSTKEY" > ~/.ssh/known_hosts
      - name: Rsync lobby
        run: |
          rsync -az --delete --exclude node_modules --exclude .env \
            -e "ssh -i ~/.ssh/deploy_key -o StrictHostKeyChecking=yes" \
            lobby/ ${{ secrets.DEPLOY_USER }}@${{ secrets.DEPLOY_HOST }}:/opt/maze/lobby/
      - name: Install deps and restart
        run: |
          ssh -i ~/.ssh/deploy_key -o StrictHostKeyChecking=yes \
            ${{ secrets.DEPLOY_USER }}@${{ secrets.DEPLOY_HOST }} \
            'cd /opt/maze/lobby && npm ci --omit=dev && sudo systemctl restart maze-lobby'
```
(`DEPLOY_HOST_KEY` format is "ssh-ed25519 AAAA..." without hostname. The server needs a sudoers rule limited to `systemctl restart maze-lobby`; or use `restrict,command=` in authorized_keys. Deploying the WebGL client: add a second rsync of `webgl-build/` to the nginx web root, and rsync the Linux server build to `/opt/maze/server/`.)

Optional GameCI job (only if you have a working license strategy; shown for completeness, unverified for Personal in 2026):
```yaml
  unity-webgl:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with: { lfs: true }
      - uses: actions/cache@v4
        with:
          path: Library
          key: Library-WebGL-${{ hashFiles('Assets/**', 'Packages/**', 'ProjectSettings/**') }}
      - uses: game-ci/unity-builder@v4
        env:
          UNITY_LICENSE: ${{ secrets.UNITY_LICENSE }}
          UNITY_EMAIL: ${{ secrets.UNITY_EMAIL }}
          UNITY_PASSWORD: ${{ secrets.UNITY_PASSWORD }}
        with:
          targetPlatform: WebGL
      - uses: actions/upload-artifact@v4
        with: { name: Build-WebGL, path: build/WebGL }
```
A Linux dedicated-server build is `targetPlatform: StandaloneLinux64` with the Dedicated Server build target/subtarget; how GameCI exposes that subtarget (e.g. via custom build method) was not verified.
- Recommendation for the guide: build Unity locally, commit nothing large, upload via `rsync`/scp; use CI for lobby only. GitHub-hosted runners are free for public repos; private-repo free minutes are limited (limit numbers not verified).

### Gaps
- GameCI Personal-license status in 2026 unresolved: could not confirm whether PR #246 merged or whether game.ci docs were updated; the docs pages fetched in search were possibly stale.
- Latest major versions of `actions/*`, `game-ci/unity-builder` not verified; v4 is the version in the cited docs for GameCI.
- GitHub free-minute quotas not verified.

## 7. Pitfalls

### Takeaway
Most failures are environmental (versions, caching, networking, quotas) rather than code; keep one checklist for each.

### Cited Findings
- IL2CPP always strips managed bytecode; reflection-reached types need `link.xml`; higher stripping levels can alter reflection behaviour; Unity suggests disabling Strip Engine Code when debugging (affects engine code only). — [Unity WebGL code stripping 6000.0](https://docs.unity.com/en-us/engine/6000.0/manual/platform-specific/webgl/building-distribution/distributionsize-codestripping); [web optimization player](https://docs.unity3d.com/2023.2/Documentation/Manual/web-optimization-player.html)
- Oracle Always Free: compute is idle if over 7 days 95th-percentile CPU and network (and memory on A1) are all below 20%; may be reclaimed (stopped); threshold was 10% then 15% (Apr 2023), now 20%; PAYG upgrade is the commonly cited avoidance. — [Oracle Always Free docs](https://docs.oracle.com/en-us/iaas/Content/FreeTier/resourceref.htm); [51sec blog](https://blog.51sec.org/2023/02/oracle-cloud-cleaning-up-idle-compute.html)
- Let's Encrypt duplicate-cert limit 5/week (see section 4). — [LE community](https://community.letsencrypt.org/t/understanding-certificate-limits/140807)
- Hidden-tab timeouts, `runInBackground` limits (section 3). 
- Unity WebGL Brotli/headers issues (section 2). 
- NGO RPC stripping: search found no NGO-specific reports; treat as unconfirmed.

### Inferences
(General knowledge unless noted.)
- Client/server version mismatch: NGO connection approval has no built-in build check; add a protocol/build version string in the connection payload and reject mismatches; browser caches serving an old client against a new server is the usual cause. Use cache-busting and `Cache-Control: no-cache` on index/loader.
- Mono vs IL2CPP: WebGL is IL2CPP only; Linux server can be Mono or IL2CPP; code behaving differently (reflection, generics AOT) can differ, so test the actual server build; keep NGO `[ServerRpc]` code in assemblies not excluded by stripping; add `link.xml` if RPC not found.
- WebGL memory: browsers cap wasm heap (mobile Safari especially); keep initial memory modest, test on target browsers; the 2GB-ish limits were not verified.
- Time zones: run server in UTC, send timestamps as UTC/epoch ms, never use `DateTime.Now` for logic.
- Oracle idle reclaim: keep real load or use PAYG (stay in free limits); ARM (A1) vs x86 AMD micro: Unity Linux server builds are x86_64, so an ARM VM cannot run them without emulation (general knowledge; verify Unity's current Linux ARM support).
- DuckDNS token leakage: see section 4. Let's Encrypt: use staging while iterating; do not delete `/etc/letsencrypt` or Caddy's data dir between redeploys (duplicate limit); DuckDNS and other shared suffixes have their own LE limits via the Public Suffix List (duckdns.org is on the PSL, which gives you your own subdomain limits - unverified).
- VirtualBox: NAT hides the VM (needs port forwarding); bridged exposes it on LAN; Wi-Fi adapters often can't bridge on laptops (they may drop frames from a second MAC) - use NAT with port forwarding or host-only plus forwarding; Windows firewall prompts on Unity.exe/Editor and blocks inbound unless allowed.
- Browser testing against self-signed certs fails for WebSockets silently: accept the cert in a tab first, or use mkcert/real domain.
- Don't test on `localhost` over http then deploy to https and assume parity.

### Gaps
- No sourced evidence for NGO RPC stripping failures, WebGL memory limits, VirtualBox/Wi-Fi bridging, Windows firewall or DuckDNS facts; all above are general knowledge.
- Unity Linux ARM server support status not checked.

## 8. Pre-flight verification checklists

### Takeaway
One short checklist per stage lets the reader prove each layer before moving on.

### Cited Findings
- None specific; checklist items rely on facts cited in sections 1-7.

### Inferences
(Authored; items reference the cited facts above.)
1. Local dev: NGO `Use Web Sockets` enabled both sides; server listening on WS; MPPM clients connect; version string matches; test in WebGL build from `http://localhost` before TLS.
2. VM/DNS: DNS A record resolves to VM IP (`dig +short HOST`); Oracle security list and ufw allow 80/443; time synced (`timedatectl`); idle-reclaim plan in place.
3. TLS/proxy: certificate issued (staging first); `openssl s_client` shows valid chain and expiry; `curl -I https://HOST` returns 200; `.wasm` MIME and `.br`/`.gz` Content-Encoding correct; HTTP/80 redirects to 443; HSTS only after confirming.
4. WebSocket path: `curl` upgrade returns 101; wrong Origin rejected; `proxy_read_timeout` raised; DevTools WS tab shows frames; no mixed-content warnings.
5. Services: runs as non-root; `systemctl is-enabled` and `Restart=` tested by `kill -9`; `systemd-analyze security` reviewed; journal shows logs; reboot test brings everything back.
6. Security: SSH password login disabled and tested from a second session; fail2ban active; unattended-upgrades on; no secrets in repo (`git log -p | grep -i token`); tokens validated server-side; RPC checks present; connection limits tested.
7. Gameplay resilience: hide tab for 10 min in Chrome, Firefox, Safari and verify reconnect/grace behaviour; kill server mid-game and verify client message; redeploy and verify cache-busting.
8. CI/CD: deploy key is restricted and server host key pinned; PR run does not deploy; failed tests block deploy; rollback = redeploy previous commit.

### Gaps
- None beyond those listed in earlier sections.
