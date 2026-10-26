# Free Unity WebGL multiplayer netcode options

Note: research done 2026-10-26 via web search only. WebFetch failed (DNS errors) for every domain, so all claims come from search-result summaries, not full-page reads. Re-verify pricing/versions on live pages before committing.

## Q1: NGO + Unity Transport (UTP) WebSocket support; can a WebGL build be HOST?

### Takeaway
UTP 2.0+ (Unity 2022.2+; 2.5/2.6 require 2022.3+) supports WebSockets, with or without TLS, but in WebGL only as client. A WebGL build cannot listen, so it cannot be a true host. The only official host path is Unity Relay (wss).

### Cited Findings
- Transport 2.0 gives a WebSocket implementation with or without TLS, usable on every platform as client or server and on WebGL as client only — [Unity Transport 2.0 announcement](https://discussions.unity.com/t/unity-transport-is-now-available-as-a-2-0-pre-release-version-package/912509/1)
- Manuals for 2.5 and 2.6 repeat: "For WebGL, only WebSocket connections in client mode are supported (it is however still possible to host games if using Unity Relay)" — [Transport 2.5 manual](https://docs.unity3d.com/Packages/com.unity.transport@2.5/manual/), [2.6 manual](https://docs.unity3d.com/Packages/com.unity.transport@2.6/manual)
- Transport 2.x needs Unity 2022.2+; 2.5/2.6 need 2022.3+ — same manuals
- Relay + NGO on web: upgrade Transport to 2.0.0+ and set the Unity Transport component to use WebSockets; the Relay host setup lists a wss connection type — [Unity Relay and NGO tutorial](https://docs.unity.com/en-us/mps-sdk/tutorials/relay-and-ngo.md)
- Community option: azure-web-pubsub-transport, a WebSocket transport for NGO claiming WebGL support (status unchecked) — [GitHub](https://github.com/albertxavier100/azure-web-pubsub-transport)

### Inferences
- Browsers cannot open listening sockets, so "WebGL host" always means traffic through a relay (Unity Relay, or your own WebSocket server relaying). Relay's WebSocket path is the official workaround; a native or dedicated server remains the robust option.
- Whether a WebGL client can be the Relay host in practice was not verified beyond the docs' statement.

### Gaps
- Exact NGO version numbers with WebGL support, and known open NGO WebGL bugs: not found (docs pages unfetchable).
- WebRTC-based NGO transports: not researched.

## Q2: Other stacks (WebGL / WSS / free limits / self-host)

### Takeaway
Mirror (SimpleWebTransport) and FishNet (Bayou, a SimpleWebTransport fork) are free, open source, WebGL-capable client-side via WebSockets, and self-hostable on a dedicated server. Photon, Unity Relay, Normcore are hosted with CCU caps. Colyseus (MIT) and Nakama are open source and self-hostable.

### Cited Findings
- Mirror SimpleWebTransport: websocket transport usable in WebGL builds; supports wss — [SimpleWebTransport repo](https://github.com/MirrorNetworking/SimpleWebTransport)
- Mirror docs say the built-in SSL settings are obsolete and may be removed; recommend a reverse proxy for WebGL/wss — [Mirror WebSockets Transport docs](https://mirror-networking.gitbook.io/docs/manual/transports/websockets-transport)
- Unity forum report of a headless Mirror host on Linux serving WebGL clients (author said unfinished) — [Unity Discussions](https://discussions.unity.com/t/engine-for-browser-multiplayer-game-using-webgl/816327)
- FishNet Bayou: WebGL transport, repurposed SimpleWebTransport, separate download (not bundled), replaces Tugboat; has "Enable WSS" and SSL certificate settings — [Bayou docs](https://fish-networking.gitbook.io/docs/manual/components/transports/bayou)
- FishNet headless server guides (Edgegap): enable Start On Headless, match port, use WS protocol; PlayFlow handles TLS for you — [Edgegap FishNet WebGL](https://docs.edgegap.com/docs/sample-projects/fishnet-webgl), [PlayFlow guide](https://fish-networking.gitbook.io/docs/guides/server-hosting/services/playflow-cloud/easy-webgl-hosting-with-playflow)
- Photon: official pricing lists a 20 CCU free tier for development/non-commercial use (PUN, 60 GB/month cap) and a free 100 CCU plan for Fusion/Quantum, one app per customer — [Photon pricing](https://doc.photonengine.com/zh-cn/photon/current/pricing), [Photon blog](https://blog.photonengine.com/?p=5466). A Photon staff forum post says the 100 CCU option is a one-time 12-month subscription, conflicting with the "free" framing — [Photon forum](https://forum.photonengine.com/discussion/20628/how-to-publish-a-multiplayer-game-on-steam-cost-effective). Third-party article: [supercraft](https://crux.supercraft.host/blog/photon-fusion-pricing-2026/) (secondary). Not self-hostable; WebGL support not confirmed in my results.
- Unity Relay: free up to 50 average monthly CCU, then $0.16 per extra average CCU (support article dated Aug 2023); bandwidth 3 GiB/CCU capped at 150 GiB/month free, then $0.09/GiB US/EU, $0.16/GiB Asia/AU; Lobby 10 GiB/month per region group — [Unity support](https://support.unity.com/hc/en-us/articles/4410136449812-How-is-the-Relay-Service-Priced), [Unity pricing](https://unity.com/products/gaming-services/pricing). Hosted only; needs a Unity account, a credit card/billing may be required for overage (unverified).
- Normcore: free Prototype plan 30 CCU, 10 rooms, 1-hour sessions, 50 room hours, 120 GB; paid from $49/month; WebGL support is alpha and needs contacting Normcore (WebRTC-based) — [Normcore pricing](https://normcore.io/pricing), [Normcore WebGL docs](https://docs.normcore.io/platforms/webgl). Hosted only.
- Colyseus: MIT, self-hostable Node.js; Unity SDK (io.colyseus.sdk) on OpenUPM; WebGL support for the SDK not confirmed — [OpenUPM](https://openupm.com/packages/io.colyseus.sdk), [self-host guide](https://localtonet.com/blog/self-host-colyseus-behind-nat-localtonet)
- Nakama: open source Go server; an old forum post says the Unity client supports WebGL (unconfirmed, license not verified in results) — [Unity forum](https://discussions.unity.com/t/open-source-backend-for-a-game/649373)

### Inferences
- Fully free with no caps: Mirror+SimpleWebTransport, FishNet+Bayou, Colyseus, Nakama, NGO+UTP on your own server. Your only cost is hosting the server and a TLS endpoint.
- Capped: Photon (20/100 CCU), Unity Relay (50 avg CCU free), Normcore (30 CCU, alpha WebGL).

### Gaps
- Netcode for Entities WebGL: search found no mention of WebGL support in its docs; treat as unsupported/unverified. Mirror+WebRTC, Unity Matchmaker free limits, Photon WebGL specifics: not found.
- Current 2026 version numbers for Mirror, FishNet, NGO: not found.

## Q3: Dedicated Linux server + WebGL client, and hidden issues

### Takeaway
Pattern: Linux Dedicated Server build listens on WebSocket (ws), a TLS-terminating reverse proxy provides wss, WebGL client connects as client. Background-tab throttling is the main documented runtime issue.

### Cited Findings
- Unity docs: when a tab is not visible, content updates only about once per second in most browsers; Application.runInBackground is the documented option but browsers may still throttle — [Unity WebGL performance docs](https://docs.unity.com/en-us/engine/6000.6/manual/platform-specific/webgl/develop/performance)
- Old forum report: with Photon's WebSocket, switching tabs disconnected the player and runInBackground did not work in browser — [Unity forum](https://forum.unity.com/threads/webgl-issues.319584/)
- Vendor blog advice: handle visibility change, reconnect and request full state resync on return — [bugnet](https://bugnet.io/blog/how-to-fix-unity-webgl-game-frozen-after-tab-throttling-in-background)
- Edgegap/PlayFlow guides show headless WebSocket servers in containers with TLS handled by the platform (links above)

### Inferences
- Mixed content: an https page (e.g. GitHub Pages/itch.io) can only open wss, so a ws-only server won't work. For no-domain wss, a free tunnel/hostname with a valid cert (e.g. Cloudflare quick tunnel) is needed; I did not verify this in sources.
- WebGL is single-threaded with no raw TCP/UDP, so only WebSocket (or WebRTC) transports apply.

### Gaps
- IL2CPP, compression and browser-caching pitfalls, Cloudflare Tunnel specifics, free hosting options: not researched or sources not found.
