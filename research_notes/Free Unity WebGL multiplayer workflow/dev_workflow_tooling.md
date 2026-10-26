# Free Unity WebGL multiplayer dev/test/deploy workflow (VirtualBox Linux VM)

Research date: 2026-10-26. Research was limited to about 15 search calls. Direct fetches of docs.unity3d.com and game.ci failed (DNS), so all findings come from search-result summaries. Items marked "uncited" in Inferences are general engineering knowledge and were not verified in this session.

## 1. Unity licensing and version choice

### Takeaway
Unity Personal is free up to US$200,000 trailing-12-month revenue or funding, and the splash screen became optional for Unity 6 projects (announced 2025-11-10). Unity 6.3 LTS (supported to Dec 2027) is the natural pick today. Unity 6.0 LTS support ends Oct 2026. NGO 2.x on Unity Transport 2.x supports WebGL via WebSockets.

### Cited Findings
- Unity's pricing-updates page lists the 2026 Personal threshold as $200,000 annual revenue and funding. The Personal section says "Splash screen optional with Unity 6", marked "Available now", dated Nov 10, 2025 — [Unity pricing updates](https://unity.com/pricing-updates)
- Plus is no longer offered; the Personal change effectively folds Plus into Personal — [Gamedevbeginner](https://gamedevbeginner.com/is-unity-free/)
- The limit is evaluated per individual or employing company over a rolling 12 months. If one team member exceeds the limit, the whole team must upgrade — [CyberPost](https://cyberpost.co/can-i-sell-a-game-made-in-unity/) (secondary source)
- One older CyberPost article says the splash is mandatory for Personal. It predates the Nov 2025 change and is contradicted by [Unity pricing updates](https://unity.com/pricing-updates)
- Unity 6.3 LTS is supported until December 2027 (two-year LTS; enterprise and industry get an extra year). Unity 6.0 LTS is supported through October 2026 — [Unity 6 support](https://unity.com/releases/unity-6/support), [Unity 6 release](https://unity.com/unity-6-release)
- Unity 6.6 exists as an alpha/preview line, and its docs have a Dedicated Server page — [Unity 6.6 docs](https://docs.unity.com/en-us/engine/6000.6/manual/platform-specific/dedicated-server/build)
- NGO docs: WebGL needs NGO 1.2.0+ and Unity Transport 2.0.0+. NGO 2.x supports Unity 6.0+ and pairs with Transport 2.x — [NGO 2.8 manual](https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.8/manual/index.html), [Transport 2.6 manual](https://docs.unity3d.com/Packages/com.unity.transport@2.6/manual)
- For WebGL, set `UseWebSockets = true` on the Unity Transport component. On an HTTPS page the endpoint must be `wss`, because insecure WebSockets are blocked — [Unity Relay + NGO](https://docs.unity.com/relay/relay-and-ngo.htm), [Unity Discussions](https://discussions.unity.com/t/unity-netcode-for-gameobjects-on-webgl/936202)
- Colyseus Unity SDK is installed via the Package Manager git URL `#upm` branch. Colyseus docs mention Multiplayer Play Mode with Unity 6000.1.0b1+ — [Colyseus Unity SDK](https://docs.colyseus.io/getting-started/unity-sdk)
- WebGPU in 6.6 is not default (WebGL2 remains default) — [Cinevva article](https://app.cinevva.com/news/2026-09-01-unity-6-6-webgpu-production) (secondary source)

### Inferences
- Recommend Unity 6.3 LTS (6000.3.x) for a new project, since 6.0 LTS support ends this month. Pin the exact patch version in `ProjectSettings/ProjectVersion.txt` and in CI.
- Choose the networking stack by architecture. If the Node matchmaker plus a custom or Colyseus room server holds the game logic, no Unity Netcode is needed in the WebGL client. If a Unity dedicated server is used, use NGO 2.x plus Transport 2.x with WebSockets.
- The exact latest NGO and Transport patch numbers were not found. Read them from Package Manager at project start and lock them in `Packages/packages-lock.json`.

### Gaps
- Exact current NGO and Transport patch versions.
- Whether a Personal user can build the Dedicated Server target without extra modules. The Linux Dedicated Server module is installed via Hub, and no licensing restriction was found.
- Whether Unity Transport WebSocket server mode on Linux dedicated server has remaining limitations. Not verified.

## 2. Build pipeline (Dedicated Server, WebGL, CI)

### Takeaway
Linux server builds use `-buildTarget Linux64 -standaloneBuildSubtarget Server` (or a Linux Server build profile). GameCI works with Personal licenses only via a manually obtained .ulf stored as a secret, which is fragile. GitHub-hosted Linux runners are free for public repos and have a monthly quota on private ones.

### Cited Findings
- CLI example: `-buildTarget Linux64 -standaloneBuildSubtarget Server`. The editor path is Build Profiles, then a Linux Server profile. Unity defines the `UNITY_SERVER` scripting symbol for that platform. A scripted build sets the subtarget to Server — [Unity 6.2 dedicated server build](https://docs.unity3d.com/6000.2/Documentation/Manual/dedicated-server-build.html), [6.6](https://docs.unity.com/en-us/engine/6000.6/manual/platform-specific/dedicated-server/build)
- GameCI Personal activation: create a .ulf with Unity Hub (Preferences > Licenses > Add > free personal license). Linux path is `~/.local/share/unity3d/Unity/Unity_lic.ulf`. The file may not be created even when Hub shows a license. Store it as the `UNITY_LICENSE` secret, plus `UNITY_EMAIL` and `UNITY_PASSWORD` — [GameCI activation](https://game.ci/docs/github/activation/), [GameCI builder](https://game.ci/docs/github/builder/)
- GameCI's older alf-to-ulf flow no longer works for Personal licenses (docs issue #408). A user reports pasting the .ulf content directly into `UNITY_LICENSE` works (unverified user report, Unity 6000.0.34f1) — [game-ci/documentation #408](https://github.com/game-ci/documentation/issues/408)
- Personal licenses used in CI expire after some time and the activation step must be repeated — [game-ci/unity-license-activate](https://github.com/game-ci/unity-license-activate)
- Related failures: "Missing Unity License File and no Serial was found" ([#617](https://github.com/game-ci/unity-builder/issues/617)), personal key not working in secret ([#204](https://github.com/game-ci/unity-builder/issues/204))
- GitHub Actions on private repos: free plan quota reported as 2,000 min/month, Linux multiplier 1x, Windows 2x, macOS 10x. The default spending limit of $0 blocks overage. The 2,000 figure comes from third-party material and was not confirmed on GitHub's own page — [GitHub Actions billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions), [Microsoft Learn](https://learn.microsoft.com/en-sg/training/modules/github-introduction-products/7-metered-usage-consumption-reports)
- Overage rates cited: $0.008/min Linux (2024 sources) versus $0.006 (2-core) and $0.002 (1-core) in current docs — [GitHub docs](https://docs.github.com/en/billing/concepts/product-billing/github-actions)
- WebGL server config (nginx): Brotli files need `Content-Encoding br`, nginx's own gzip off for those, and correct MIME types (`application/wasm` for .wasm). Real-world failures are wasm MIME errors and decode errors — [Unity nginx config](https://docs.unity.com/en-us/engine/6000.0/manual/platform-specific/webgl/building-distribution/server-configuration-code-samples/web-server-config-nginx), [nginx community thread](https://community.nginx.org/t/unity-webgl-drift-hunters-on-nginx-correct-headers-for-br-gz-coop-coep-getting-mime-decode-errors/6083)

### Inferences
- Simplest free pipeline: local batchmode builds via a `BuildScript.cs` (static methods for `BuildWebGL` and `BuildLinuxServer`, run with `Unity -batchmode -nographics -quit -executeMethod`). Add GameCI later. Public repo gives free CI minutes, but GameCI Personal activation is the weak point.
- For WebGL settings (uncited, verify in the 6.3 docs): use Brotli or Gzip compression with matching nginx headers, or Decompression Fallback if headers can't be controlled. Use Managed Stripping Level Medium or High and test after stripping, with a modest initial memory size and growth enabled.
- The Unity license is still needed on the build machine. The VM is a good place to build Linux server builds only if it has the Editor and a license, so building on the dev machine and shipping artifacts is simpler.

### Gaps
- Specific WebGL memory-size and stripping recommendations from Unity 6.3 docs were not retrieved.
- Free-minute limits for public repos were not directly confirmed in this session (commonly stated as free and unlimited for standard runners, but not verified here).
- Whether GameCI activation with a pasted .ulf currently works end to end in 2026 is unconfirmed.

## 3. Local multi-client testing

### Takeaway
Multiplayer Play Mode (MPPM) gives up to 4 players locally in the Editor, but it runs Editor player instances and not real WebGL builds. For true WebGL testing, serve the build locally and use several browser windows or profiles. Hidden tabs throttle to about 1 update per second.

### Cited Findings
- MPPM supports up to four players on one device (Main Editor plus 3 Virtual Players), designed for small-scale local testing, and is resource heavy — [MPPM docs](https://mp-docs.dl.it.unity3d.com/mppm/current/about/)
- Unity says content keeps running when focus is lost only if "Run In Background" / `Application.runInBackground` is enabled. Even then browsers throttle hidden tabs to about one update per second, and `Time.time` progresses slower than normal because `Time.maximumDeltaTime` is lower than 1 s. Keep `Application.targetFrameRate = -1` on WebGL — [Unity 6.2 WebGL performance](https://docs.unity3d.com/6000.2/Documentation/Manual/webgl-performance.html), [6.4](https://docs.unity3d.com/Manual/webgl-performance)
- A practitioner reports about 1 FPS in inactive tabs and races/freezes on wake, and suggests visibility detection — [DEV Community](https://dev.to/furic/unity-webgl-background-tabs-autoplay-performance-fix-1m9k)

### Inferences
- MPPM tests game logic and netcode, not WebGL-specific behavior (WebSockets, throttling, CORS, memory). Do final checks in real browsers.
- To avoid false bugs: place test windows side by side in separate (not tabbed) browser windows, because a window that is visible is not throttled while a background tab is. Use different browser profiles or incognito for separate client identities. Consider an automated headless client (Node WebSocket bot) to simulate extra players.
- Server must tolerate a client that stops sending for around 1 s without being treated as disconnected. Tune heartbeat timeouts accordingly (uncited).
- ParrelSync was not researched. Note that clones are Editor-only and MPPM has largely replaced it on Unity 6 (uncited).

### Gaps
- MPPM and WebGL build-target compatibility not confirmed.
- ParrelSync Unity 6 status not researched.
- Whether a WebSocket stays alive and receives messages in a throttled background tab was not found.

## 4. Deploying to the VirtualBox Linux VM

### Takeaway
Use Ubuntu Server LTS in VirtualBox with bridged networking (or NAT plus port forwarding), nginx as reverse proxy and static host for the WebGL build, and systemd units for the Node matchmaker and the Unity server binary. nginx needs explicit WebSocket upgrade headers and a longer read timeout.

### Cited Findings
- nginx strips `Connection` and `Upgrade` by default. Use `proxy_http_version 1.1`, re-add those headers (the `map $http_upgrade` pattern), and raise `proxy_read_timeout` because the default closes idle proxied connections after 60 s. Verify the handshake returns HTTP 101. Socket.IO guidance: read timeout above heartbeat interval plus timeout (default 45 s) — [NGINX WebSocket proxy (zaiste)](https://zaiste.net/posts/nginx-websockets-proxy-nodejs/), [GetPageSpeed](https://www.getpagespeed.com/server-setup/nginx/nginx-websocket-proxy), [OneUptime](https://oneuptime.com/blog/post/2025-12-16-websocket-400-bad-request-nginx/markdown)
- Multiple Node instances with Socket.IO need sticky sessions (for example `ip_hash`) — [itechguides](https://www.itechguides.com/how-to-set-up-an-nginx-reverse-proxy-for-socket-io/)
- Unity nginx WebGL header/MIME config, see section 2.

### Inferences (all uncited, standard practice, verify against Ubuntu docs)
- Distro: Ubuntu Server 24.04 LTS (or Debian 12). Install nginx, Node LTS, ufw, rsync.
- VirtualBox: bridged adapter is simplest, since the VM gets a LAN IP reachable from host and other devices. With NAT, add port forwarding rules for 80/443 and the game port. Host-only adapter is fine for host-only testing.
- Deploy: `rsync -az --delete Build/WebGL/ vm:/var/www/maze/` and `rsync` the Linux server build to `/opt/maze-server/` (`chmod +x`). Shared folders work but need Guest Additions and complicate permissions, so prefer rsync/scp over SSH.
- systemd: one unit each for `maze-matchmaker.service` (Node) and `maze-gameserver.service` (Unity, run with `-batchmode -nographics -logFile -`), `Restart=on-failure`, `User=` non-root, logs via `journalctl -u`. systemd is enough, and pm2 is optional.
- ufw: allow 22, 80, 443 only and keep game/matchmaker ports on localhost behind nginx. If the Unity server needs a direct UDP/TCP port, open only that.
- HTTPS: `wss` is needed when the page is HTTPS. Use a self-signed or mkcert certificate on a LAN (browsers need to trust it) or plain HTTP/ws for LAN-only testing. Note that plain-HTTP is not a secure context.
- Docker is optional; a Linux Dedicated Server build runs fine in a plain Ubuntu container image. Skip it until multiple server instances are needed.

### Gaps
- No source retrieved for systemd unit specifics, VirtualBox networking, or Docker for Unity servers. Items above are from general knowledge.

## 5. Node.js matchmaker, CORS, and React wrapper

### Takeaway
A small Express/Fastify service with REST for lobby/matchmaking, plus ws or Colyseus for real-time, is enough. Cross-origin UnityWebRequest from WebGL requires server-side CORS, including a correct OPTIONS preflight response. react-unity-webgl is optional and adds version-matching and browser-compat caveats.

### Cited Findings
- Colyseus: Unity SDK with `new ColyseusClient("ws://...")` (or wss), `Create` and `JoinOrCreate` matchmaking calls, automatic state patches at `patchRate` default 50 ms — [Colyseus Unity SDK](https://docs.colyseus.io/getting-started/unity-sdk)
- Colyseus 0.17 docs say only the TypeScript SDK is supported for now, so check Unity SDK compatibility before using 0.17 — same source (search summary, treat as to-verify)
- UnityWebRequest cross-origin calls from WebGL require CORS on the server; fix by serving from the same domain or enabling CORS — [Unity WebGL networking](https://docs.unity3d.com/ja//Manual/webgl-networking.html)
- Preflight pitfalls: OPTIONS returning 400/403 breaks requests that work in the Editor. Setting headers in client code does nothing — [itch.io thread](https://itch.io/t/367395/unity-webgl-and-cors), [Unity Discussions](https://discussions.unity.com/t/unity-web-request-cors-error-on-webgl-hosted-on-aws-but-works-on-editor-253728/253728)
- A Unity 6 test of Web builds calling an Express API on EC2 exists (content not read) — [Classmethod](https://dev.classmethod.jp/articles/unity6-web-api-ec2-cors-test/)
- react-unity-webgl: install a module version matching the Unity version; docs list v10 as current, and the 8.x line targets Unity 2020 to 2022. WebGL 1 is slated for removal. Unity web needs a 64-bit WebAssembly browser and does not target mobile — [react-unity-webgl installation](https://react-unity-webgl.dev/docs/getting-started/installation), [browser compatibility](https://react-unity-webgl.dev/docs/miscellaneous-instructions/browser-compatibility), [8.x intro](https://react-unity-webgl.dev/docs/8.x.x/introduction)
- A Unity 6 WebGL+React integration report shows input (mic/typing) problems, though for another SDK — [Convai forum](https://forum.convai.com/t/issues-with-unity-webgl-react-integration/3169)

### Inferences
- Easiest CORS: serve the WebGL build and the matchmaker API from the same nginx origin (`/` static, `/api/` and `/ws/` proxied). That removes CORS entirely. Otherwise mount Express `cors` middleware before routes with an explicit origin allowlist (uncited).
- Share config via a single `shared/config.json` (ports, protocol version, max players) read by Node and embedded in Unity via `StreamingAssets` or a Resources TextAsset, or served by `GET /api/config` (uncited).
- A React lobby is worthwhile only if you need rich lobby UI (accounts, room lists, chat). For a maze prototype, a plain HTML page plus Unity UI is faster. If used, expect keyboard-focus capture issues between React and canvas, and the version coupling above.

### Gaps
- Express/Fastify CORS code was not verified from a source. No source on ws vs Socket.IO from WebGL (Unity has no official Socket.IO client; community libraries vary).
- react-unity-webgl Unity 6.3 support not confirmed.

## 6. Recommended repo layout and milestones (synthesis, uncited)

Layout:
```
repo/
  unity/            # Unity project (Assets, Packages, ProjectSettings) + Editor/BuildScript.cs
  server/matchmaker # Node (Express/Fastify + ws/Colyseus), package.json
  shared/           # config.json, protocol docs
  deploy/           # nginx.conf, *.service, deploy.sh (rsync), ufw.sh
  .github/workflows # optional CI
```
Milestones:
1. Empty repo, Unity 6.3 LTS project, Unity .gitignore, Git LFS if needed; commit lockfile.
2. Node matchmaker with `/health` and room create/join REST; test with curl.
3. Unity WebGL client calls `/health` via UnityWebRequest; verify CORS in a browser.
4. WebSocket link (ws or Colyseus) between client and matchmaker; two browser windows join one room.
5. Batchmode build script; build WebGL locally; serve with local nginx.
6. VM provisioning: Ubuntu, nginx, Node, ufw, systemd units; `deploy.sh` rsync.
7. Linux dedicated server build (if used) launched by matchmaker or systemd; client connects over wss.
8. Optional: GameCI workflow, React lobby.

## 7. Common pitfalls (mix of cited and uncited)

### Cited Findings
- Hidden tab throttling to about 1 update/s and slow `Time.time` — [Unity WebGL performance](https://docs.unity3d.com/6000.2/Documentation/Manual/webgl-performance.html)
- `ws://` blocked from an HTTPS page; need `wss` — [Unity Discussions](https://discussions.unity.com/t/unity-netcode-for-gameobjects-on-webgl/936202)
- Missing `application/wasm` MIME or `Content-Encoding` headers cause load failures — [Unity nginx config](https://docs.unity.com/en-us/engine/6000.0/manual/platform-specific/webgl/building-distribution/server-configuration-code-samples/web-server-config-nginx)
- CORS preflight (OPTIONS) failures — [itch.io](https://itch.io/t/367395/unity-webgl-and-cors)
- nginx drops idle WebSockets after 60 s without `proxy_read_timeout` — [GetPageSpeed](https://www.getpagespeed.com/server-setup/nginx/nginx-websocket-proxy)
- GameCI Personal license expiry/activation breakage — [unity-license-activate](https://github.com/game-ci/unity-license-activate)
- react-unity-webgl version must match Unity — [installation](https://react-unity-webgl.dev/docs/getting-started/installation)

### Inferences (uncited, from general experience)
- WebGL has no raw UDP/TCP sockets and no threads, so only WebSocket (or WebRTC) transports work. System.Net sockets and `System.Threading` do not work.
- Stripping can remove reflection-used types; test builds with stripping on and use link.xml.
- Browser memory limits: large initial heap or high-memory content can fail to load on low-RAM machines.
- Cached builds: browsers cache old builds; use content-hashed filenames or cache-control headers when redeploying.
- Server clock/tick should not depend on client frame rate. Autoplay audio requires a user gesture.
- Server builds on Linux: forgetting `chmod +x`, missing `-nographics`, or logging to a file only (use `-logFile -` for journald).

### Gaps
- No single "experienced developer" retrospective from 2024-2026 was found; the uncited items should be verified before being stated as fact.
