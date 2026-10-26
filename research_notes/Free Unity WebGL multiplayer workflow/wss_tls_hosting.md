# WSS/TLS and free hosting for a Unity WebGL multiplayer game

Research caveat: direct page fetches (WebFetch/curl) were blocked in this environment (DNS/proxy 403), so all findings come from WebSearch result summaries of the cited pages. Items not confirmed from a source are in Inferences or Gaps and need verification before use.

## Browser mixed-content rules (https page vs ws://, http page vs ws://, LAN IPs)

### Takeaway
An https page that opens ws:// is blocked as mixed content; loopback (127.0.0.1/localhost) is treated as "potentially trustworthy" and is the documented exception, with Firefox support dated. Private LAN IPs are not covered by any exemption I could confirm. Current Chrome/Safari specifics are not verified.

### Cited Findings
- A page served over HTTPS that opens an insecure WebSocket gets a "Mixed Content" error and an uncaught SecurityError; the usual fix is wss:// — [Tencent Cloud Q&A](https://developer.cloud.tencent.com/ask/sof/101081573)
- Let's Encrypt says modern browsers consider http://127.0.0.1:8000/ "potentially trustworthy" because it is loopback, but forbid HTTPS pages from making XHR/WebSocket requests to non-secure URLs (browser not named) — [Let's Encrypt: Certificates for localhost](https://letsencrypt.org/tr/docs/certificates-for-localhost/)
- Firefox: Bugzilla reports a localhost exception fixed in Firefox 55; a separate bug about treating http://localhost (by name) as mixed content is marked fixed in Firefox 84 — [Bug 903966](https://bugzilla.mozilla.org/show_bug.cgi?id=903966), [Bug 1488740](https://bugzilla.mozilla.org/show_bug.cgi?id=1488740) (version numbers as reported by the search summary)
- Safari: only evidence is an Apple Developer Forums thread (WKWebView) reporting ws:// blocked as mixed content with no resolution — [Apple forums](https://developer.apple.com/forums/thread/120869)
- Chrome has a separate "Local Network Access" permission layer, distinct from CORS and mixed content, and behaviour is version-sensitive (third-party 2026 guide, says verify against Chrome docs) — [localtonet](https://localtonet.com/blog/chrome-local-network-access-apis-websockets)
- Unity Transport docs: if the WebGL game is distributed over HTTPS you must use WSS to reach the server — [Unity Transport 2.5 WebSockets](https://docs.unity3d.com/Packages/com.unity.transport@2.5/manual/websockets.html)

### Inferences
- If the page is plain http://, ws:// is not mixed content (standard rule; not confirmed by a fetched source here). So a fully-http LAN dev setup (page on http://192.168.x.x, server ws://192.168.x.x) should work with no certificates; but then Chrome Local Network Access prompts may apply for private-IP targets from public pages.
- Simplest dev path: serve page over http and use ws://. Only need TLS when the page itself must be https (public hosting on GitHub Pages/itch.io etc.).

### Gaps
- Official Chrome/web.dev/MDN statement on ws://localhost from https pages; current Safari behaviour; exact Chrome LNA rules for private IPs and WebSockets.

## Dev TLS setup (mkcert, self-signed, Unity Transport/NGO/Mirror, reverse proxy)

### Takeaway
Unity Transport 2.x supports WSS natively on the server (cert + key from files), but a Unity forum report shows the "Use Encryption" route not working out of the box and Unity staff suggesting a TLS-terminating reverse proxy. Self-signed certs do not work in browsers by default, so a public-CA cert or a locally trusted CA is needed.

### Cited Findings
- Client side needs only the server name (`WithSecureClientParameters(serverName: ...)`, `WebSocketNetworkInterface`); browser/OS trust store verifies — [Unity Transport 2.5 WebSockets](https://docs.unity3d.com/Packages/com.unity.transport@2.5/manual/websockets.html)
- Server side loads fullchain.pem/privkey.pem via `WithSecureServerParameters(certificate, privateKey)`; docs say self-signed certs won't work in browsers by default, use a public CA such as Let's Encrypt — same source
- Forum thread (NGO 2.2 / UTP 2.4): ticking "Use Encryption" was not enough for WSS; Unity staff suggested a reverse proxy that terminates TLS and forwards plain traffic, and alternatively `UnityTransport.SetServerSecrets` with the server's certs; noted Multiplay cannot provide certs to game servers — [Unity discussions](https://discussions.unity.com/t/issues-setting-up-encrypted-websocket-connection-with-unity-transport/1620104)
- WebSocket implementation works as client or server on all platforms except WebGL, which is client-only (so a Linux dedicated server + WebGL client is the supported shape) — [Unity Transport 2.0 announcement](https://discussions.unity.com/t/unity-transport-is-now-available-as-a-2-0-pre-release-version-package/912509/1)
- Relay guide: for web platforms use Unity Transport 2.0.0+ and WebSockets (`UseWebSockets = true`, "wss") — [Unity Relay + NGO](https://docs.unity.com/en-us/mps-sdk/tutorials/relay-and-ngo)
- FishNet Bayou transport has an "Enable WSS" option with certificate path settings — [FishNet docs](https://fish-networking.gitbook.io/docs/manual/components/transports/bayou)
- Caddy can obtain Let's Encrypt certs for a DuckDNS name via DNS-01 using the caddy-dns/duckdns module; works even if the A record points to a LAN IP — [caddy-dns/duckdns](https://github.com/caddy-dns/duckdns), [Caddy community](https://caddy.community/t/dns-challenge-with-duckdns/14994)

### Inferences
- Reverse proxy (Caddy: automatic certs, 2-line config, native WebSocket proxying) is likely simpler and more robust than in-engine TLS, and keeps cert renewal out of the game process. Not directly benchmarked in sources.
- mkcert (local CA) works for localhost/LAN IPs on the dev machine; other devices need the root CA installed manually. I did not verify mkcert docs this session.

### Gaps
- mkcert GitHub docs, nginx/HAProxy docs, Mirror (SimpleWeb transport) TLS settings: not retrieved. Exact NGO inspector fields for cert/key (the `UseEncryption` checkbox, `SetServerSecrets`) not confirmed from official docs.

## VirtualBox networking

### Takeaway
Bridged gives the VM its own LAN IP (reachable by other devices directly); NAT needs per-port forwarding rules and is reached via the host IP; host-only reaches only host and VMs.

### Cited Findings
- Bridged: guest gets own MAC/IP on LAN; reachable from other machines without port forwarding; downside is exposure and address-pool conflicts — [Cleverence summary of VirtualBox manual 6.5](https://www.cleverence.com/articles/oracle-documentation/6-5-bridged-networking-oracle-vm-virtualbox-3742/)
- NAT: inbound blocked unless port-forward rules map host port to guest port; LAN peers connect via host IP — [DevGenius comparison](https://blog.devgenius.io/choosing-the-right-virtualbox-network-adapter-nat-host-only-or-bridged-188003a260de), [KodeKloud notes](https://notes.kodekloud.com/docs/DevOps-Pre-Requisite-Course/Lab-Setup/Virtual-Box-Networking)
- Host-only: connects only host and VMs, no external network — same sources

### Inferences
- For testing from host + phones on the LAN: bridged (wifi bridging can be flaky) or NAT with forward rules (leave host IP blank/0.0.0.0 to accept LAN, and open host firewall). Host-only suffices for host-only testing.

### Gaps
- Forwarding rule bind-address and Wi-Fi bridging caveats not confirmed from the VirtualBox manual.

## Free domain / TLS options

### Takeaway
Let's Encrypt now issues free IP-address certs (GA 15 Jan 2026, 160-hour lifetime), so a bare public IP can get a trusted cert with no domain. DuckDNS + Caddy DNS-01 is a workable free-domain route. sslip.io is unreliable for LE because of shared-domain rate limits. Tunnels vary in caps.

### Cited Findings
- Let's Encrypt IP address certs (IPv4 and IPv6) and short-lived certs became generally available January 15, 2026; IP certs are 160 hours (~6 days) with mandatory short lifetime; short-lived domain certs opt-in via the "shortlived" profile; some ACME clients need updates — [Linuxiac](https://linuxiac.com/lets-encrypt-launches-ip-address-certificates-with-6-day-lifetimes/), [Let's Encrypt post](https://letsencrypt.org/2025/07/01/issuing-our-first-ip-address-certificate.html), [StackScale](https://www.stackscale.com/blog/lets-encrypt-opens-the-door-to-https-by-ip/)
- sslip.io: users hit LE 429 "too many certificates (50000) already issued for sslip.io"; OpenVidu recommends own domain; shared domains not on the PSL suffer from rate limits — [OpenVidu thread](https://openvidu.discourse.group/t/deployment-without-domain/5474), [LE community](https://community.letsencrypt.org/t/how-order-certificate-for-shared-domain/116317)
- Cloudflare Quick Tunnels: hard limit of 200 in-flight requests (429 beyond), no SSE support, no SLA/uptime guarantee; Cloudflare says to use a named tunnel with an account for production — [Cloudflare Quick Tunnels](https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/)
- Quick-tunnel WebSocket support: only a third-party guide says it works — [StackHarbor](https://stackharbor.com/en/knowledge-base/cf-quick-tunnel-trycloudflare-guide/) (unverified)
- ngrok free plan (official docs, internally inconsistent): 1 GB/month data out, 20,000 HTTP requests/month, 5,000 TCP connections, one auto-assigned dev domain; endpoint cap stated as 3 on one page and 1 on another; static-domain statements conflict — [ngrok free plan limits](https://ngrok.com/docs/pricing-limits/free-plan-limits), [pricing limits](https://ngrok.com/docs/pricing-limits)
- ngrok interstitial warning page on free HTML traffic: third-party claim only — [Merginit](https://merginit.com/blog/19062026-free-developer-tunnels-comparison)
- Tailscale Funnel: ports 443, 8443, 10000 only; TLS only; names only under your tailnet's ts.net; non-configurable bandwidth limits (numbers unpublished); needs v1.38.3+, MagicDNS, HTTPS certs, funnel node attribute; `--tcp` mode forwards raw TCP — [Tailscale Funnel docs](https://tailscale.com/kb/1223/funnel), [SSD Nodes](https://www.ssdnodes.com/learn/tailscale-funnel-limits-and-ports)

### Inferences
- Because IP certs last 6 days, they need an ACME client that auto-renews (Caddy/certbot with recent version); HTTP-01 or TLS-ALPN-01 on the public IP is likely required (validation methods for IP certs not confirmed here).
- ngrok free's 1 GB/month cap makes it poor for a game's WebGL download + traffic; use only for short tests.
- Tailscale Funnel gives a stable free ts.net HTTPS name that can front a WebSocket server (likely, WS support unconfirmed).

### Gaps
- DuckDNS, No-IP, FreeDNS limits (No-IP free hostname 30-day confirmation, etc.), LE rate-limit numbers, DNS-01/HTTP-01 constraints, nip.io, localhost.run: not retrieved. Cloudflare quick-tunnel WebSocket support not confirmed officially. ngrok current free-plan truth must be checked live.

## Free permanent hosting (Oracle Always Free, GitHub Pages, itch.io, Cloudflare Pages)

### Takeaway
Oracle Always Free A1 can host both the WebGL files and the WSS server (the only option here that runs a game server). Static hosts (GitHub Pages, itch.io, Cloudflare Pages) can only serve the WebGL build; the server must live elsewhere with a wss:// endpoint.

### Cited Findings
- Oracle Always Free Ampere A1: 4 OCPUs and 24 GB memory total; idle instances may be reclaimed if over 7 days the 95th-percentile CPU, network and (A1) memory are all below 20% (older sources cite 10%/15%) — [Oracle Always Free resources](https://docs.oracle.com/en-us/iaas/Content/FreeTier/resourceref.htm), [51sec blog](https://blog.51sec.org/2023/02/oracle-cloud-cleaning-up-idle-compute.html)
- Users report mixed experiences of reclaim; keeping utilisation above threshold is the safeguard — [space-node guide](https://space-node.net/blog/oracle-cloud-free-vps-guide-2026)
- itch.io: community reports conflict on Brotli (some say it fails, others say it works); gzip is the safer choice; one poster says itch does on-demand compression so upload with compression disabled — [itch.io Brotli thread](https://itch.io/t/1909856/brotli-support-for-unity-webgl-builds), [itch.io post](https://itch.io/post/1849809) (user claims, not official)

### Inferences
- Region capacity for A1 ("out of capacity") and the 200 GB block storage, egress allowance, and credit-card verification: not verified here; flag as risks.
- A page hosted on https (GitHub Pages/itch.io) forces wss:// to a server with a trusted cert, so Oracle VM + Caddy (+ DuckDNS name, or LE IP cert) is the fully free combination.

### Gaps
- GitHub Pages header/compression behavior, Cloudflare Pages `_headers`, itch.io official docs, Oracle capacity/egress/Always Free E2 Micro details: not confirmed from sources.

## Unity WebGL build hidden issues

### Takeaway
Pre-compressed builds need the web server to send matching Content-Encoding headers; otherwise enable Decompression Fallback (adds .unityweb extension, larger loader, slower).

### Cited Findings
- Server must serve compressed files with appropriate headers, e.g. `.br` with `Content-Encoding: br` — [Unity WebGL deploying](https://docs.unity3d.com/2020.3/Documentation/Manual/webgl-deploying.html)
- Decompression Fallback is a Player Setting (Publishing Settings), disabled by default, embeds a JS decompressor into the build, causes a larger loader and less efficient loading, adds .unityweb extension; Unity suggests it when server config is unavailable — same source
- Forum: wrong Content-Encoding (gzip vs br) breaks loading; workaround is decompress files and strip the extension — [Unity forum](https://discussions.unity.com/t/configurate-brotli-compression/682537), [Unity forum](https://discussions.unity.com/t/webgl-build-doesnt-load-in-browser/803777)

### Inferences
- GitHub Pages cannot set custom headers (general knowledge, unverified), so use Decompression Fallback or Compression Format Disabled there.

### Gaps
- COOP/COEP (threads only), file:// failure, .wasm MIME type (application/wasm) all rely on general knowledge; not sourced here.
