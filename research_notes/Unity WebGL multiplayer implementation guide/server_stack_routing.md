# Server stack and room routing for Unity WebGL (NGO) behind one TLS endpoint

Scope: how a browser Unity WebGL client reaches many room processes through one TLS endpoint, nginx vs Caddy, a Node.js lobby, systemd, layout, sizing, security.
Evidence labels: **[source]** = read in primary source text this session (GitHub raw files); **[search]** = web-search summary only (docs.unity3d.com, discussions.unity.com, mirror gitbook, duckdns.org and caddyserver.com all failed DNS in this sandbox); **[tested]** = I ran it here (Node 22.22, lobby with a stand-in room server; nginx and Caddy are NOT installed here, so no proxy config was executed). Every config below is marked "verified against docs" (only from search summaries of docs) or "untested".

## Does Unity Transport / NGO support a custom WebSocket path?

### Takeaway
**Yes. This overturns the earlier hypothesis.** NGO 2.11.0 (2026-03-19) added `UnityTransport.ConnectionData.WebSocketPath`, and Unity Transport has taken a path via `WithWebSocketParameters(path)` since 2.2.0 (2023-12). So `wss://HOST/r/<id>` routing is feasible for NGO clients, including WebGL, on a recent NGO. One gotcha: `SetConnectionData(...)` rebuilds `ConnectionData` and drops the path (see below), so set the path after it. Whether the WebGL JS plugin really puts the path on the wire is not proven by me and needs the 10-minute spike below.

### Cited Findings
- NGO CHANGELOG 2.11.0 (2026-03-19): "Added a `WebSocketPath` field to `UnityTransport.ConnectionData` (which also shows up in the inspector if 'Use WebSockets' is checked) that controls the path clients will connect to and servers/hosts will listen on when using WebSockets. (#3901)" — [NGO CHANGELOG, branch develop-2.0.0](https://raw.githubusercontent.com/Unity-Technologies/com.unity.netcode.gameobjects/develop-2.0.0/com.unity.netcode.gameobjects/CHANGELOG.md) [source]
- The same branch's package.json is NGO 2.13.4, depending on `com.unity.transport` 2.6.0, Unity 6000.0 (its changelog lists 2.13.3 on 2026-09-14) — [package.json](https://raw.githubusercontent.com/Unity-Technologies/com.unity.netcode.gameobjects/develop-2.0.0/com.unity.netcode.gameobjects/package.json) [source]
- Code in `UnityTransport.cs`: field `public string WebSocketPath;` tooltip "Path to connect to or listen on when using WebSockets. Defaults to "/" if not set."; default `ConnectionAddressData { Address = "127.0.0.1", Port = 7777, WebSocketPath = "/", ... }`; and in driver setup: `if (m_UseWebSockets && m_ProtocolType != ProtocolType.RelayUnityTransport && !string.IsNullOrWhiteSpace(ConnectionData.WebSocketPath)) settings.WithWebSocketParameters(path: ConnectionData.WebSocketPath);` — [UnityTransport.cs](https://raw.githubusercontent.com/Unity-Technologies/com.unity.netcode.gameobjects/develop-2.0.0/com.unity.netcode.gameobjects/Runtime/Transports/UTP/UnityTransport.cs) [source]
- GOTCHA in the same file: `SetConnectionData(bool forceOverrideCommandLineArgs, string ipv4Address, ushort port, string listenAddress)` assigns `ConnectionData = new ConnectionAddressData { Address, Port, ServerListenAddress, ClientBindPort }`. `WebSocketPath` is not copied, so it becomes null and the default "/" is used. Set `ConnectionData.WebSocketPath` AFTER calling `SetConnectionData`. [source; my reading of the code, untested in a build]
- Same file: `-port` and `-ip` command-line args override port/address in `SetConnectionData` unless `forceOverrideCommandLineArgs` is true. So `-port 7003` from the lobby works without custom parsing. [source]
- Same file: `address` doc says the "remote IP address (despite the name, can be an IPv6 address or a domain name)". So a hostname is accepted as `Address`; no `wss://` URL is accepted. [source]
- Same file: `#if UNITY_WEBGL && !UNITY_EDITOR` hosting a server throws "WebGL as a server is not supported by Unity Transport, outside the Editor." On WebGL, WebSocket interface is always used. The `#if UNITY_SERVER` block only complains about WebSockets in the Relay case, i.e. a plain (non-Relay) Linux server with `UseWebSockets` is allowed. [source]
- Unity Transport changelog 2.2.0 (2023-12-01): the WebSocket URL path can be set via `NetworkSettings.WithWebSocketParameters`; clients connect to that path, servers accept only connections made to that path — [Transport 2.0.0 release-notes page via search](https://mp-docs.dl.it.unity3d.com/transport/2.0.0/release-notes/transport-2-0-0/) [search]
- Before 2.11 a Unity staff member said in a forum thread that NGO had no path option and a custom `INetworkStreamDriverConstructor` was needed — [Unity Discussions thread](https://discussions.unity.com/t/networkmanager-how-to-set-path-of-unitytransport-websocket-tcp/1594491) [search]; superseded by 2.11.0 above.
- Secure client: only the server name is needed, `WithSecureClientParameters(serverName: "your-domain.com")`, trust comes from the OS/browser store; an https page needs WSS — [Unity Transport 2.5 websockets manual](https://docs.unity3d.com/Packages/com.unity.transport@2.5/manual/websockets.html) [search]. In NGO this corresponds to `UseEncryption = true` plus `SetClientSecrets(serverCommonName)` (tooltip in source: "enable DTLS for UDP and TLS for Websocket") [source].
- Unity's manual says that without a certificate on the game server you need Relay or your own TLS-terminating reverse proxy, "for advanced users" — [manual](https://docs.unity3d.com/Packages/com.unity.transport@2.5/manual/websockets.html) [search]
- Query strings: no text found that either supports or forbids `?x=y` in the path. The path goes straight into the HTTP GET line in principle, but I did not verify it. Do not depend on it; put the token in the NGO `ConnectionApproval` payload instead (works regardless). [gap]
- Mirror SimpleWebTransport: `ClientConnect(string hostname)` builds `new UriBuilder { Scheme = ws|wss, Host = hostname }` and only sets Port (modes: specify port, match webpage protocol, default same as server). There is no path setting. A `ClientConnect(Uri uri)` overload exists, so code can pass a full URI with a path. `clientUseWss` ("Useful when client needs to connect using wss when TLS is outside of transport") and `sslEnabled` ("never with reverse proxy") exist. — [SimpleWebTransport.cs](https://raw.githubusercontent.com/MirrorNetworking/Mirror/master/Assets/Mirror/Transports/SimpleWeb/SimpleWebTransport.cs) [source]. Mirror is therefore NOT better for path routing than NGO >= 2.11.
- Conflicting claim: a PlayFab issue says a WebSocket proxy "completes the upgrade but the Unity Transport server discards the data" because UTP expects "UTP packet-formatted messages" and "Unity Transport does not support https:// or wss://" — [PlayFab issue #9](https://github.com/PlayFab/MultiplayerServerSecureWebsocket/issues/9) [source: issue text, no maintainer reply]. This is one reporter's diagnosis of their setup, not an Unity statement; Unity's own docs describe UTP WebSockets as a normal WS server carrying UTP packets in binary frames and recommend a TLS-terminating reverse proxy. Treat as unresolved until your spike passes (a pure TLS-terminating proxy that forwards frames unchanged should be transparent).
- NGO has one `NetworkManager` per process; I found no source for hosting many independent NGO sessions in one server process. Multi-room-in-one-process (option e) is not supported as far as I found. [gap]

### Inferences
- Because path routing is possible, the port-range fallback is no longer the primary plan. Keep it as plan B only if the WebGL client turns out to ignore the path.
- Path-only routing needs the server to listen on "/" (default), because the proxy rewrites `/r/70xx` to `/`. That avoids setting a per-room server path.
- Pin NGO >= 2.11.0 (current develop-2.0.0 branch reports 2.13.x) and verify `com.unity.transport` resolves to 2.6.x in `packages-lock.json`.

### Gaps
- Not run: a real WebGL build. Spike: build WebGL client with `UseEncryption=true`, Address=host, Port=443, `WebSocketPath="/r/7000"`; run `nc`/`tcpdump` or nginx `$request_uri` in an access log; success = a log line with `GET /r/7000`.
- Unknown whether the Unity WebGL JS WebSocket plugin appends the path (the plugin source is inside the closed-source `com.unity.transport` package).
- Query-string behaviour unknown.

## Routing options compared (when path routing fails or is undesirable)

### Takeaway
Ranking: (1) path `/r/<port>` through nginx, (2) proxy-terminated public port range, (3) Node upgrade-sniffing proxy, (4) subdomain per room, (5) header/cookie tricks, (6) multi-room single process (not viable).

### Cited Findings
- nginx closes a proxied WebSocket after 60 s without data from the backend by default; fix by raising `proxy_read_timeout` or having the backend send WS ping frames; the official pattern uses `proxy_http_version 1.1` plus Upgrade/Connection headers via a `map $http_upgrade $connection_upgrade` — [nginx WebSocket docs, mirrors via search](https://freenginx.org/hg/nginx-site/file/6fcdefd1df4e/xml/en/docs/http/websocket.xml?revcount=3) [search]
- DuckDNS: "xxx.yyy.duckdns.org shares the same TXT record as yyy.duckdns.org", the update API takes `domains`, `token`, `txt`, optional `clear`; DuckDNS does not allow further subdomain labels as separate records, names are `[a-z0-9-]`; wildcard + apex in one cert fails because only one TXT record per name, so request them separately — [DuckDNS spec](https://www.Duckdns.org/spec.jsp), [OPNsense issue](https://github.com/opnsense/plugins/issues/2060), [Let's Encrypt community](https://community.letsencrypt.org/t/unable-to-create-wildcard-certificate-on-duckdns/190287) [search]. The free-account domain limit (commonly said to be 5) was NOT confirmed. Note `a.myname.duckdns.org` resolves via DuckDNS wildcard behaviour reportedly (the spec text above implies subdomains inherit), unverified.

### Inferences
- (a) **Port range**: nginx `server { listen 8000..; ssl }` per port or one `stream`/`http` block per port. A static generated file for 100 ports is easy (script below) and needs no reload per room because all 100 listeners exist permanently and just proxy to 127.0.0.1:7000+n (connection refused when the room is dead). Cost: firewall must open the range; corporate networks that only allow 443 fail. Config generator is below. untested.
- (b) **Subdomain per room** needs a wildcard cert (DNS-01). Caddy `dns duckdns` plugin supports it, but DuckDNS TXT limitation and the free-domain cap make it fragile; a path is simpler. Rejected for the maze.
- (c) **Header/Cookie/Origin/Sec-WebSocket-Protocol**: browsers send cookies for the page origin automatically, so a cookie set by `POST /api/rooms/:id/join` (Set-Cookie, `Path=/`, `Secure`, `SameSite=Lax`) would be sent on the wss handshake to the same host, and nginx could `map $cookie_room $room_port`. This works without Unity cooperation, but only one room per browser profile at a time (two tabs conflict) and cookies are weaker than a path. Sec-WebSocket-Protocol can only be set by page JS through the WebSocket constructor; Unity's plugin builds the socket itself, so not controllable. Origin is fixed by the page origin. Feasible only as a fallback; untested.
- (d) **Node upgrade-sniffing proxy** (code below, `tools/ws-router.mjs`): a Node `http` server on 127.0.0.1 that on `'upgrade'` reads the path/cookie, opens a TCP socket to the room and writes the raw request then pipes both sockets. Costs a Node hop per player; no WS parsing so Unity frames pass byte-for-byte. Useful only if nginx cannot route what you need. Written, not run.
- (e) A single Unity process hosting many NGO sessions: not supported (see above); also one crashed room would kill all. Reject.

### Gaps
- Free DuckDNS domain cap not verified.

## nginx vs Caddy for this job

### Takeaway
Use **nginx** for the VM: path routing by `location ~ ^/r/(?<p>70\d\d)$` with the port taken from the URL needs no dynamic upstream and no reload; it serves Unity static builds with fine control of `Content-Encoding`. Choose **Caddy** only if you want zero-config certificates (DuckDNS needs a custom xcaddy build) or Let's Encrypt IP certificates; it handles WebSockets automatically.

### Cited Findings
- Caddy `reverse_proxy` proxies WebSockets automatically, with `flush_interval -1` suggested by guides for WS/SSE; `stream_close_delay` keeps long streams alive across config reloads; `reload` equals POST to `/load` and is graceful for normal HTTP, but long-lived WebSockets can be closed when the old handler unloads — [Caddy docs](https://caddyserver.com/docs/caddyfile/directives/reverse_proxy), [community thread](https://caddy.community/t/reverse-proxy-websocket-dropouts-and-502-errors-on-long-lived-connections/33849) [search]
- Caddy with DuckDNS DNS-01 needs a custom build: `xcaddy build --with github.com/caddy-dns/duckdns`; Caddyfile uses `tls { dns duckdns {$DUCKDNS_TOKEN} }`; a 2025 log showed module v0.4.0; there were bug reports of the plugin using the wrong zone — [Caddy community](https://caddy.community/t/caddy-as-reverse-proxy-with-duckdns-package/11993), [issue thread](https://caddy.community/t/dns-challenge-on-duck-dns-fails-with-connection-refused/21913) [search]
- Caddy dynamic upstreams exist for SRV/A lookups, refreshed every minute by default; frequent routing changes via the admin API are discouraged by one community post (use granular PATCH) — [Caddy PR #4470](https://github.com/caddyserver/caddy/pull/4470), [community](https://caddy.community/t/way-to-achieve-zero-downtime-for-existing-connections-when-dynamic-patch-maps/33753) [search]
- nginx facts: 60 s default read timeout, upgrade `map` (above) [search].

### Inferences
| Concern | nginx | Caddy |
|---|---|---|
| Dynamic rooms | Port from URL regex: no reload. Or `map` include + `nginx -s reload` | Port from URL via `reverse_proxy 127.0.0.1:{re.room.1}` placeholders (no reload); admin API otherwise |
| Reload effect on live WS | Old workers keep serving existing connections until they close (set `worker_shutdown_timeout` if you want a cap) | Reload may drop long WS unless `stream_close_delay` |
| TLS | certbot or acme.sh; DuckDNS via hook scripts | Automatic; DuckDNS DNS-01 needs xcaddy build |
| WS defaults | Need Upgrade headers, 60 s idle timeout | Automatic; no idle timeout by default (not verified) |
| Raw TCP/TLS passthrough | `stream` module (ngx_stream_ssl_preread can route on SNI) | Needs `caddy-l4` plugin |
| Static Unity build + `Content-Encoding: br/gzip` | `location ~ \.br$` with `add_header Content-Encoding br; default_type` (config below) | `file_server { precompressed br gzip }` plus manual header for non-matching names |
| Footprint | ~5-10 MB, ubiquitous | Single binary, ~40 MB |

### Gaps
- caddyserver.com pages were unreachable; Caddy placeholders in the Caddyfile below are from my own knowledge of its syntax, not read this session: mark untested.

## Working proxy configs (nginx recommended, Caddy alternative)

### Takeaway
The recommended single-endpoint config routes `wss://HOST/r/<roomPort>` to `127.0.0.1:<roomPort>` with a regex location, so creating or killing a room never touches nginx. Status of every file: **untested** (nginx/Caddy not installable in this sandbox); syntax follows nginx docs as summarised via search ("verified against docs" only for the WebSocket map/Upgrade/timeouts pattern).

### Cited Findings
- WebSocket proxy pattern (`proxy_http_version 1.1`, `Upgrade`/`Connection` from a `map`, raise `proxy_read_timeout`) — [nginx WebSocket doc mirror](https://freenginx.org/hg/nginx-site/file/6fcdefd1df4e/xml/en/docs/http/websocket.xml?revcount=3) [search]
- Unity Transport needs the server path to equal the request path: servers only accept connections made to their configured path [search, Transport changelog above], so the proxy rewrites `/r/70xx` to `/` (server keeps default path "/").
- Mirror's own reverse-proxy docs exist for nginx but the page could not be fetched — [Mirror nginx guide](https://mirror-networking.gitbook.io/docs/manual/transports/websockets-transport/reverse-proxy/linux/nginx) [search]

### Inferences
**/etc/nginx/sites-available/maze.conf (untested; mostly verified-against-docs pattern)**

```nginx
# http-context map (put in conf.d/ or at top of this file; sites-enabled is included inside http{})
map $http_upgrade $connection_upgrade {
    default upgrade;
    ''      close;
}

# per-IP connection and request limits (small VM DoS protection)
limit_conn_zone $binary_remote_addr zone=perip:10m;
limit_req_zone  $binary_remote_addr zone=api:10m rate=10r/s;

server {
    listen 80;
    listen [::]:80;
    server_name maze.example.duckdns.org;
    location /.well-known/acme-challenge/ { root /var/www/acme; }
    location / { return 301 https://$host$request_uri; }
}

server {
    listen 443 ssl;
    listen [::]:443 ssl;
    http2 on;                     # nginx >= 1.25.1; older: "listen 443 ssl http2;"
    server_name maze.example.duckdns.org;

    ssl_certificate     /etc/letsencrypt/live/maze.example.duckdns.org/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/maze.example.duckdns.org/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_session_cache shared:SSL:10m;

    add_header X-Content-Type-Options nosniff always;
    server_tokens off;
    limit_conn perip 40;

    # --- Unity WebGL build (static) ---
    root /opt/maze/web;
    index index.html;

    location / {
        try_files $uri =404;
        add_header Cache-Control "no-cache";           # index.html: always revalidate
    }

    # Unity "Build/" files compressed by Unity. Correct Content-Encoding + real MIME type.
    location ~ \.wasm\.br$   { types {} default_type application/wasm;             add_header Content-Encoding br;   add_header Cache-Control "public, max-age=31536000, immutable"; }
    location ~ \.js\.br$     { types {} default_type application/javascript;       add_header Content-Encoding br;   add_header Cache-Control "public, max-age=31536000, immutable"; }
    location ~ \.data\.br$   { types {} default_type application/octet-stream;     add_header Content-Encoding br;   add_header Cache-Control "public, max-age=31536000, immutable"; }
    location ~ \.wasm\.gz$   { types {} default_type application/wasm;             add_header Content-Encoding gzip; add_header Cache-Control "public, max-age=31536000, immutable"; }
    location ~ \.js\.gz$     { types {} default_type application/javascript;       add_header Content-Encoding gzip; add_header Cache-Control "public, max-age=31536000, immutable"; }
    location ~ \.data\.gz$   { types {} default_type application/octet-stream;     add_header Content-Encoding gzip; add_header Cache-Control "public, max-age=31536000, immutable"; }
    location ~ \.wasm$       { types {} default_type application/wasm; }

    # --- Lobby REST API ---
    location /api/ {
        limit_req zone=api burst=20 nodelay;
        proxy_pass http://127.0.0.1:3000;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $remote_addr;   # overwrite, never append client-supplied value
        proxy_set_header X-Forwarded-Proto $scheme;
        client_max_body_size 4k;
    }
    location /internal/ { return 404; }                  # heartbeat endpoint is loopback only

    # --- Game rooms: wss://HOST/r/7000 .. /r/7099 -> ws://127.0.0.1:<port>/ ---
    location ~ ^/r/(?<room_port>70[0-9][0-9])$ {
        proxy_pass http://127.0.0.1:$room_port/;         # URI "/" replaces /r/70xx; IP literal, so no resolver needed
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection $connection_upgrade;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $remote_addr;
        proxy_read_timeout 3600s;                        # default is 60s: idle WS would be cut
        proxy_send_timeout 3600s;
        proxy_buffering off;
        limit_conn perip 12;
        client_max_body_size 0;
    }
}
```

Notes: (1) `proxy_pass` with variable + URI: nginx docs state the URI is then sent as written, replacing the request URI (my recollection, not re-read; verify with `curl -i --http1.1 -H 'Upgrade: websocket' ...` and the room server's access logs). (2) If the regex-with-variable form is rejected by your nginx, use the generated `map $room_port`/include approach: `map $uri $room_upstream { include /etc/nginx/maze-rooms.map; }` and `nginx -s reload` from the lobby via a sudoers rule; not needed otherwise. (3) A dead room gives 502; the client sees a failed connect.

**Port-range fallback**: generate with the script below; open `8000-8099/tcp` in ufw; the client then uses `Port = 8000+n` and path "/" with no lobby-side nginx work (untested; tool tested only for output shape).

**Caddyfile (untested; needs xcaddy build with `github.com/caddy-dns/duckdns` for DNS-01, otherwise drop the `tls` block and Caddy uses HTTP-01 automatically on a public name)**

```caddyfile
{
    email you@example.com
}

maze.example.duckdns.org {
    tls {
        dns duckdns {env.DUCKDNS_TOKEN}
    }

    encode zstd gzip                       # NOT for Unity .br/.gz files, see precompressed below

    # rooms: /r/7003 -> 127.0.0.1:7003/
    @room path_regexp room ^/r/(70[0-9][0-9])$
    handle @room {
        rewrite * /
        reverse_proxy 127.0.0.1:{re.room.1} {
            stream_close_delay 5m          # keep WebSockets across config reloads
        }
    }

    handle /api/* {
        reverse_proxy 127.0.0.1:3000
    }
    respond /internal/* 404

    handle {
        root * /opt/maze/web
        @br  path *.br
        @gz  path *.gz
        header @br Content-Encoding br
        header @gz Content-Encoding gzip
        @wasm path *.wasm.br *.wasm.gz *.wasm
        header @wasm Content-Type application/wasm
        file_server
    }
}
```
Caveat: `{re.room.1}` inside the upstream after a `rewrite` is untested and the placeholder scope may differ; if it fails, capture the port with a `map` first. Reverse-proxy to a placeholder port with a plain `{re.…}` placeholder in `reverse_proxy` is documented as supported for upstreams in general, but I could not read the docs.

**TLS issuance on nginx (untested)**: for a public DuckDNS name, `certbot certonly --webroot -w /var/www/acme -d maze.example.duckdns.org` (HTTP-01). On a LAN-only VM use DNS-01 with `certbot-dns-duckdns` ([PyPI](https://pypi.org/project/certbot-dns-duckdns)) [search].

**Fallback tools (tested only as Node scripts)**

**tools/gen-port-range.mjs (port-range config generator)**

```js
// Fallback (a): emit nginx server blocks for a public wss port range -> loopback room ports.
// usage: node gen-port-range.mjs 8000 8099 7000 > /etc/nginx/conf.d/maze-ports.conf
const [pubStart, pubEnd, privStart] = process.argv.slice(2).map(Number);
let out = '# generated; one TLS listener per public port\n';
for (let p = pubStart; p <= pubEnd; p++) {
  const t = privStart + (p - pubStart);
  out += `server {
  listen ${p} ssl;
  server_name _;
  ssl_certificate     /etc/letsencrypt/live/maze/fullchain.pem;
  ssl_certificate_key /etc/letsencrypt/live/maze/privkey.pem;
  location / {
    proxy_pass http://127.0.0.1:${t};
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection $connection_upgrade;
    proxy_read_timeout 3600s; proxy_send_timeout 3600s;
  }
}
`;
}
process.stdout.write(out);
```

**tools/ws-router.mjs (Node upgrade router, tested: echoed a binary frame through /r/7003 and returned 404 for /r/9999)**

```js
// Fallback (d): transparent upgrade router. Listens on 127.0.0.1:7999, nginx proxies /ws there.
// Reads the room port from the request path (/r/7003) or a `room` cookie, then pipes raw bytes
// to 127.0.0.1:<port>. No WebSocket parsing: Unity frames pass through unchanged.
import http from 'node:http';
import net from 'node:net';

const PORT_MIN = 7000, PORT_MAX = 7099;
const pick = (req) => {
  const m = /^\/r\/(\d{4})$/.exec(req.url.split('?')[0]);
  const c = /(?:^|;\s*)room=(\d{4})/.exec(req.headers.cookie ?? '');
  const p = +(m?.[1] ?? c?.[1] ?? 0);
  return p >= PORT_MIN && p <= PORT_MAX ? p : 0;
};

const srv = http.createServer((_, res) => { res.writeHead(426).end('upgrade required'); });
srv.on('upgrade', (req, client, head) => {
  const port = pick(req);
  if (!port) return client.end('HTTP/1.1 404 Not Found\r\nConnection: close\r\n\r\n');
  const up = net.connect(port, '127.0.0.1', () => {
    // replay the handshake, rewriting the target path to "/" (the room server listens on "/")
    let raw = `GET / HTTP/1.1\r\n`;
    for (let i = 0; i < req.rawHeaders.length; i += 2) raw += `${req.rawHeaders[i]}: ${req.rawHeaders[i + 1]}\r\n`;
    up.write(raw + '\r\n');
    if (head?.length) up.write(head);
    client.pipe(up).pipe(client);
  });
  const kill = () => { client.destroy(); up.destroy(); };
  up.on('error', kill); client.on('error', kill); up.on('close', kill); client.on('close', kill);
});
srv.listen(7999, '127.0.0.1');
```

### Gaps
- No nginx/Caddy binary here, so no `nginx -t` or `caddy validate` was run. Run both before use.
- Caddy IP-address certificates and `shortlived` profile not verified (prior notes mark it "probable").

## Unity side: client path, server args, token validation

### Takeaway
Client sets host, port 443, TLS and the path returned by the lobby; the server reads `-port` (built into UnityTransport), validates the HMAC join token in `ConnectionApproval`, and heartbeats to the lobby.

### Cited Findings
- `-port`/`-ip` command line args are applied by `SetConnectionData` unless forced off; `WebSocketPath` is NOT preserved by `SetConnectionData` (see first section) — [UnityTransport.cs](https://raw.githubusercontent.com/Unity-Technologies/com.unity.netcode.gameobjects/develop-2.0.0/com.unity.netcode.gameobjects/Runtime/Transports/UTP/UnityTransport.cs) [source]

### Inferences
**Client (C#, untested; API names from the source above)**
```csharp
var ut = NetworkManager.Singleton.GetComponent<UnityTransport>();
ut.UseWebSockets = true;                     // forced on WebGL anyway
ut.SetConnectionData(info.host, (ushort)info.port);       // host name, 443
ut.ConnectionData.WebSocketPath = info.path;              // "/r/7003"  (AFTER SetConnectionData)
ut.UseEncryption = info.secure;                           // wss
ut.SetClientSecrets(info.host);                           // server common name
NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(info.token);
NetworkManager.Singleton.StartClient();
```
`ConnectionData` is a struct property; if the compiler complains, copy it, set the field, assign back (`var d = ut.ConnectionData; d.WebSocketPath = ...; ut.ConnectionData = d;`).

**Server (C#, untested)**: `NetworkManager.ConnectionApprovalCallback`, token = `b64url(payload).b64url(HMAC-SHA256(secret, b64url(payload)))`, payload JSON `{r,n,j,exp}`.
```csharp
void Approve(NetworkManager.ConnectionApprovalRequest req, NetworkManager.ConnectionApprovalResponse res) {
    res.Approved = false; res.CreatePlayerObject = true;
    var tok = Encoding.UTF8.GetString(req.Payload).Split('.');
    if (tok.Length != 2) return;
    using var h = new HMACSHA256(Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("ROOM_SECRET")));
    var expect = h.ComputeHash(Encoding.UTF8.GetBytes(tok[0]));
    if (!CryptographicOperations.FixedTimeEquals(expect, B64UrlDecode(tok[1]))) return;
    var p = JsonUtility.FromJson<Claims>(Encoding.UTF8.GetString(B64UrlDecode(tok[0])));
    if (p.r != roomId || p.exp < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return;
    res.Approved = connectedCount < maxPlayers;           // reject full rooms; reuse of j (jti) can be tracked in a HashSet
}
```
Server startup: parse `-roomId`, `-maxPlayers`; `Debug.Log("ROOM_READY")` after `StartServer()`; every 5 s `UnityWebRequest.Post(LOBBY_URL + "/internal/rooms/"+roomId+"/heartbeat", {"players":n})` with header `x-internal-key`. Also send NGO/UTP heartbeats via the transport defaults; nginx idle timeout (3600 s above) is far above them. Use `[Serializable] class Claims { public string r; public string n; public string j; public long exp; }`.

### Gaps
- Sample not compiled (no Unity here).

## Node.js lobby (Fastify 5)

### Takeaway
Tested here with Node 22.22 and a stand-in room server: create (201 with token and `/r/<port>` path), list, join, validation rejects bad names, empty-room reaper stopped the child, SIGTERM killed children. Not tested with the real Unity binary.

### Cited Findings (package versions queried live from the npm registry on 2026-10-26; registry JSON `latest` field)
- fastify 5.12.7, @fastify/cors 11.3.1, @fastify/rate-limit 11.2.1, @fastify/helmet 13.2.0, @fastify/static 10.1.7, ws 8.22.0, jsonwebtoken 9.0.3, pino 10.4.0 — [npm registry](https://registry.npmjs.org/fastify/latest). The lobby uses Node's built-in `crypto` HMAC tokens, so `jsonwebtoken` is not needed. Node 22 LTS was the runtime used (v22.22.0).

### Inferences
Features present: REST (`POST /api/rooms`, `GET /api/rooms`, `POST /api/rooms/:id/join`, `DELETE /api/rooms/:id` needing a valid token for that room), 5-char room codes without look-alike characters, port allocator 7000-7099 with bind test and in-memory reservation, `spawn` with `-batchmode -nographics -port -roomId -maxPlayers -logFile -`, TCP-poll readiness (also records a `ROOM_READY` stdout marker), loopback heartbeat endpoint, reaper (heartbeat lost, empty past grace), orphan cleanup from pid files plus `/proc/<pid>/cmdline`, SIGTERM handling, HMAC join tokens (60 s TTL), per-route rate limits, max rooms, no CORS by default (same origin), secrets via environment not argv (deviation from the brief's `-token` arg: argv shows in `ps`; the room secret is passed as `ROOM_SECRET` env and the per-player token goes through NGO ConnectionApproval).

**/opt/maze/lobby/package.json**

```json
{
  "name": "maze-lobby",
  "version": "1.0.0",
  "private": true,
  "type": "module",
  "engines": { "node": ">=22" },
  "scripts": { "start": "node src/server.mjs" },
  "dependencies": {
    "@fastify/cors": "11.3.1",
    "@fastify/rate-limit": "11.2.1",
    "fastify": "5.12.7"
  },
  "devDependencies": {
    "ws": "8.22.0"
  }
}
```

**src/config.mjs**

```js
import process from 'node:process';

const int = (k, d) => (process.env[k] ? parseInt(process.env[k], 10) : d);

export const cfg = {
  host: process.env.LOBBY_HOST ?? '127.0.0.1',
  port: int('LOBBY_PORT', 3000),
  serverBin: process.env.SERVER_BIN ?? '/opt/maze/server/MazeServer.x86_64',
  serverCwd: process.env.SERVER_CWD ?? '/opt/maze/server',
  portMin: int('PORT_MIN', 7000),
  portMax: int('PORT_MAX', 7099),
  maxRooms: int('MAX_ROOMS', 8),
  maxPlayers: int('MAX_PLAYERS', 8),
  // secret shared with the Unity server (env var, never argv: argv is visible in `ps`)
  tokenSecret: process.env.TOKEN_SECRET ?? '',
  internalKey: process.env.INTERNAL_KEY ?? '',
  tokenTtlSec: int('TOKEN_TTL_SEC', 60),
  readyTimeoutMs: int('READY_TIMEOUT_MS', 30000),
  emptyGraceMs: int('EMPTY_GRACE_MS', 60000),
  heartbeatTimeoutMs: int('HEARTBEAT_TIMEOUT_MS', 30000),
  // what the browser must use to reach a room through nginx
  publicHost: process.env.PUBLIC_HOST ?? 'localhost',
  publicPort: int('PUBLIC_PORT', 443),
  publicTls: (process.env.PUBLIC_TLS ?? 'true') === 'true',
  pathPrefix: process.env.ROOM_PATH_PREFIX ?? '/r/',
  runDir: process.env.RUN_DIR ?? '/run/maze-lobby',
  allowedOrigin: process.env.ALLOWED_ORIGIN ?? '', // empty = same-origin only, no CORS headers
};

if (cfg.tokenSecret.length < 32 || cfg.internalKey.length < 16) {
  console.error('TOKEN_SECRET (>=32 chars) and INTERNAL_KEY (>=16 chars) are required');
  process.exit(1);
}
```

**src/tokens.mjs**

```js
import { createHmac, randomBytes, timingSafeEqual } from 'node:crypto';
import { cfg } from './config.mjs';

const b64u = (b) => Buffer.from(b).toString('base64url');
const sign = (data) => createHmac('sha256', cfg.tokenSecret).update(data).digest();

// token = base64url(JSON payload) + "." + base64url(HMAC-SHA256(secret, base64url(payload)))
export function makeJoinToken(roomId, name) {
  const payload = b64u(JSON.stringify({
    r: roomId,
    n: name,
    j: randomBytes(8).toString('hex'),
    exp: Math.floor(Date.now() / 1000) + cfg.tokenTtlSec,
  }));
  return `${payload}.${b64u(sign(payload))}`;
}

// The Unity server does the same check in ConnectionApproval; this is used by tests.
export function verifyJoinToken(token, roomId) {
  const [payload, mac] = String(token).split('.');
  if (!payload || !mac) return null;
  const good = sign(payload);
  const got = Buffer.from(mac, 'base64url');
  if (got.length !== good.length || !timingSafeEqual(got, good)) return null;
  const p = JSON.parse(Buffer.from(payload, 'base64url').toString());
  if (p.r !== roomId || p.exp < Date.now() / 1000) return null;
  return p;
}
```

**src/rooms.mjs**

```js
import { spawn } from 'node:child_process';
import { randomInt } from 'node:crypto';
import net from 'node:net';
import fs from 'node:fs';
import path from 'node:path';
import { cfg } from './config.mjs';

const ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'; // no 0/O/1/I
export const rooms = new Map(); // id -> room
const usedPorts = new Set();

export function newRoomId() {
  for (let i = 0; i < 50; i++) {
    let id = '';
    for (let k = 0; k < 5; k++) id += ALPHABET[randomInt(ALPHABET.length)];
    if (!rooms.has(id)) return id;
  }
  throw new Error('could not allocate room id');
}

function canBind(port) {
  return new Promise((resolve) => {
    const s = net.createServer();
    s.once('error', () => resolve(false));
    s.listen(port, '127.0.0.1', () => s.close(() => resolve(true)));
  });
}

export async function allocPort() {
  for (let p = cfg.portMin; p <= cfg.portMax; p++) {
    if (usedPorts.has(p)) continue;
    usedPorts.add(p); // reserve first: avoids a race between concurrent creates
    if (await canBind(p)) return p;
    usedPorts.delete(p);
  }
  return null;
}

function waitTcp(port, timeoutMs, aborted) {
  const t0 = Date.now();
  return new Promise((resolve) => {
    const tick = () => {
      if (aborted()) return resolve(false);
      const s = net.connect(port, '127.0.0.1');
      s.once('connect', () => { s.destroy(); resolve(true); });
      s.once('error', () => {
        s.destroy();
        if (Date.now() - t0 > timeoutMs) resolve(false);
        else setTimeout(tick, 250);
      });
    };
    tick();
  });
}

const pidFile = (id) => path.join(cfg.runDir, `${id}.pid`);

export async function createRoom({ name, maxPlayers }, log) {
  if (rooms.size >= cfg.maxRooms) return { error: 'server_full' };
  const port = await allocPort();
  if (port === null) return { error: 'no_free_port' };
  const id = newRoomId();
  const room = {
    id, port, name, maxPlayers: Math.min(maxPlayers ?? cfg.maxPlayers, cfg.maxPlayers),
    state: 'starting', players: 0, createdAt: Date.now(),
    lastHeartbeat: Date.now(), emptySince: Date.now(), child: null, exited: false,
  };
  rooms.set(id, room);

  const args = ['-batchmode', '-nographics', '-port', String(port), '-roomId', id,
    '-maxPlayers', String(room.maxPlayers), '-logFile', '-']; // '-' = stdout -> journald
  const child = spawn(cfg.serverBin, args, {
    cwd: cfg.serverCwd,
    stdio: ['ignore', 'pipe', 'pipe'],
    env: {
      // minimal env; secrets go here, not in argv
      PATH: process.env.PATH,
      HOME: cfg.serverCwd,
      ROOM_SECRET: cfg.tokenSecret,
      LOBBY_URL: `http://127.0.0.1:${cfg.port}`,
      LOBBY_INTERNAL_KEY: cfg.internalKey,
    },
  });
  room.child = child;
  try { fs.writeFileSync(pidFile(id), String(child.pid)); } catch { /* ignore */ }

  const pipeLines = (stream, level) => {
    let buf = '';
    stream.on('data', (d) => {
      buf += d;
      let i;
      while ((i = buf.indexOf('\n')) >= 0) {
        const line = buf.slice(0, i); buf = buf.slice(i + 1);
        if (line.includes('ROOM_READY')) room.markerSeen = true;
        log[level]({ room: id, port }, line);
      }
    });
  };
  pipeLines(child.stdout, 'info');
  pipeLines(child.stderr, 'warn');

  child.once('exit', (code, sig) => {
    room.exited = true;
    log.info({ room: id, code, sig }, 'room exited');
    destroyRecord(id);
  });
  child.once('error', (e) => { log.error({ room: id, err: e.message }, 'spawn failed'); room.exited = true; destroyRecord(id); });

  const ok = await waitTcp(port, cfg.readyTimeoutMs, () => room.exited);
  if (!ok) { killRoom(id, 'not_ready'); return { error: 'room_failed_to_start' }; }
  room.state = 'ready';
  room.lastHeartbeat = Date.now();
  return { room };
}

function destroyRecord(id) {
  const r = rooms.get(id);
  if (!r) return;
  usedPorts.delete(r.port);
  rooms.delete(id);
  try { fs.unlinkSync(pidFile(id)); } catch { /* ignore */ }
}

export function killRoom(id, reason = 'api') {
  const r = rooms.get(id);
  if (!r) return false;
  r.state = 'closing';
  if (r.child && !r.exited) {
    r.child.kill('SIGTERM');
    const t = setTimeout(() => { if (!r.exited) r.child.kill('SIGKILL'); }, 5000);
    t.unref();
  } else destroyRecord(id);
  return reason;
}

export function reap(log) {
  const now = Date.now();
  for (const r of rooms.values()) {
    if (r.state !== 'ready') continue;
    if (now - r.lastHeartbeat > cfg.heartbeatTimeoutMs) { log.warn({ room: r.id }, 'reap: heartbeat lost'); killRoom(r.id, 'heartbeat'); }
    else if (r.players === 0 && now - r.emptySince > cfg.emptyGraceMs) { log.info({ room: r.id }, 'reap: empty'); killRoom(r.id, 'empty'); }
  }
}

export function heartbeat(id, players) {
  const r = rooms.get(id);
  if (!r) return false;
  r.lastHeartbeat = Date.now();
  if (players !== r.players) r.players = players;
  if (players > 0) r.emptySince = 0; else if (!r.emptySince) r.emptySince = Date.now();
  return true;
}

// Startup: kill leftovers from a previous lobby that crashed (pid files + /proc check).
export function cleanOrphans(log) {
  fs.mkdirSync(cfg.runDir, { recursive: true });
  for (const f of fs.readdirSync(cfg.runDir)) {
    if (!f.endsWith('.pid')) continue;
    const file = path.join(cfg.runDir, f);
    const pid = parseInt(fs.readFileSync(file, 'utf8'), 10);
    try {
      const cmd = fs.readFileSync(`/proc/${pid}/cmdline`, 'utf8');
      if (cmd.includes(path.basename(cfg.serverBin))) { process.kill(pid, 'SIGKILL'); log.warn({ pid }, 'killed orphan'); }
    } catch { /* process gone */ }
    try { fs.unlinkSync(file); } catch { /* ignore */ }
  }
}

export async function shutdownAll(log) {
  const live = [...rooms.values()].filter((r) => r.child && !r.exited);
  live.forEach((r) => r.child.kill('SIGTERM'));
  const deadline = Date.now() + 5000;
  while (live.some((r) => !r.exited) && Date.now() < deadline) await new Promise((r) => setTimeout(r, 100));
  live.filter((r) => !r.exited).forEach((r) => { log.warn({ room: r.id }, 'SIGKILL at shutdown'); r.child.kill('SIGKILL'); });
}
```

**src/server.mjs**

```js
import Fastify from 'fastify';
import cors from '@fastify/cors';
import rateLimit from '@fastify/rate-limit';
import { timingSafeEqual } from 'node:crypto';
import { cfg } from './config.mjs';
import { makeJoinToken } from './tokens.mjs';
import { rooms, createRoom, killRoom, reap, heartbeat, cleanOrphans, shutdownAll } from './rooms.mjs';

const app = Fastify({
  logger: { level: process.env.LOG_LEVEL ?? 'info' },
  trustProxy: '127.0.0.1', // nginx on the same box sets X-Forwarded-For
  bodyLimit: 4096,
});

if (cfg.allowedOrigin) await app.register(cors, { origin: cfg.allowedOrigin, methods: ['GET', 'POST', 'DELETE'] });
await app.register(rateLimit, { global: true, max: 60, timeWindow: '1 minute' });

const ROOM_ID = { type: 'string', pattern: '^[A-Z0-9]{5}$' };
const NAME = { type: 'string', minLength: 1, maxLength: 24, pattern: '^[\\w .-]+$' };

const publicRoom = (r) => ({ id: r.id, name: r.name, players: r.players, maxPlayers: r.maxPlayers, state: r.state });
const connInfo = (r, token) => ({
  url: `${cfg.publicTls ? 'wss' : 'ws'}://${cfg.publicHost}:${cfg.publicPort}${cfg.pathPrefix}${r.port}`,
  host: cfg.publicHost, port: cfg.publicPort, secure: cfg.publicTls,
  path: `${cfg.pathPrefix}${r.port}`, token,
});

app.get('/api/health', async () => ({ ok: true, rooms: rooms.size }));

app.get('/api/rooms', async () => ({
  rooms: [...rooms.values()].filter((r) => r.state === 'ready').map(publicRoom),
}));

app.post('/api/rooms', {
  config: { rateLimit: { max: 5, timeWindow: '1 minute' } },
  schema: { body: { type: 'object', additionalProperties: false, required: ['playerName'],
    properties: { playerName: NAME, roomName: NAME, maxPlayers: { type: 'integer', minimum: 1, maximum: 16 } } } },
}, async (req, reply) => {
  const { playerName, roomName, maxPlayers } = req.body;
  const res = await createRoom({ name: roomName ?? `${playerName}'s room`, maxPlayers }, req.log);
  if (res.error) return reply.code(res.error === 'room_failed_to_start' ? 500 : 503).send({ error: res.error });
  return reply.code(201).send({ room: publicRoom(res.room), ...connInfo(res.room, makeJoinToken(res.room.id, playerName)) });
});

app.post('/api/rooms/:id/join', {
  config: { rateLimit: { max: 20, timeWindow: '1 minute' } },
  schema: { params: { type: 'object', properties: { id: ROOM_ID } },
    body: { type: 'object', additionalProperties: false, required: ['playerName'], properties: { playerName: NAME } } },
}, async (req, reply) => {
  const r = rooms.get(req.params.id.toUpperCase());
  if (!r || r.state !== 'ready') return reply.code(404).send({ error: 'no_such_room' });
  if (r.players >= r.maxPlayers) return reply.code(409).send({ error: 'room_full' });
  return { room: publicRoom(r), ...connInfo(r, makeJoinToken(r.id, req.body.playerName)) };
});

// Only the room owner / admin should delete. Here: requires a valid join token for that room.
app.delete('/api/rooms/:id', { schema: { params: { type: 'object', properties: { id: ROOM_ID } } } }, async (req, reply) => {
  const auth = String(req.headers.authorization ?? '').replace(/^Bearer /, '');
  const { verifyJoinToken } = await import('./tokens.mjs');
  if (!verifyJoinToken(auth, req.params.id)) return reply.code(403).send({ error: 'forbidden' });
  return killRoom(req.params.id) ? { ok: true } : reply.code(404).send({ error: 'no_such_room' });
});

// Called by the Unity server every ~5 s over loopback. nginx must NOT proxy /internal/.
app.post('/internal/rooms/:id/heartbeat', {
  config: { rateLimit: false },
  schema: { params: { type: 'object', properties: { id: ROOM_ID } },
    body: { type: 'object', required: ['players'], properties: { players: { type: 'integer', minimum: 0, maximum: 64 } } } },
}, async (req, reply) => {
  const k = Buffer.from(String(req.headers['x-internal-key'] ?? ''));
  const want = Buffer.from(cfg.internalKey);
  if (k.length !== want.length || !timingSafeEqual(k, want)) return reply.code(403).send();
  return heartbeat(req.params.id, req.body.players) ? { ok: true } : reply.code(404).send();
});

cleanOrphans(app.log);
const reaper = setInterval(() => reap(app.log), 5000);
reaper.unref();

let stopping = false;
async function stop(sig) {
  if (stopping) return;
  stopping = true;
  app.log.info({ sig }, 'shutting down');
  clearInterval(reaper);
  await app.close();
  await shutdownAll(app.log);
  process.exit(0);
}
process.on('SIGTERM', () => stop('SIGTERM'));
process.on('SIGINT', () => stop('SIGINT'));

await app.listen({ host: cfg.host, port: cfg.port });
```

**tools/fake-room-server.mjs (test stand-in; shows the heartbeat contract)**

```js
// Stand-in for the Unity Linux server, to test the lobby/nginx without Unity.
// Accepts the same CLI args, listens (plain ws) on 127.0.0.1:<port>, only on path "/",
// POSTs heartbeats to the lobby like the real server should.
import { WebSocketServer } from 'ws';
const a = process.argv; const arg = (n) => a[a.indexOf(n) + 1];
const port = +arg('-port'), roomId = arg('-roomId');
const wss = new WebSocketServer({ host: '127.0.0.1', port, path: '/' });
let players = 0;
wss.on('connection', (ws) => { players++; ws.on('message', (m, bin) => ws.send(m, { binary: bin })); ws.on('close', () => players--); });
console.log(`ROOM_READY ${roomId} ${port}`);
setInterval(() => fetch(`${process.env.LOBBY_URL}/internal/rooms/${roomId}/heartbeat`, {
  method: 'POST', headers: { 'content-type': 'application/json', 'x-internal-key': process.env.LOBBY_INTERNAL_KEY },
  body: JSON.stringify({ players }) }).catch(() => {}), 2000);
process.on('SIGTERM', () => process.exit(0));
```

### Gaps
- Known limits: state is in memory, so a lobby restart loses room records (children die with it under `KillMode=control-group`). Player counts come from the server's heartbeat only. `DELETE` authorisation is "any holder of a valid token for that room"; add an owner flag if needed.
- Unity may print many log lines; `-logFile -` sends the log to stdout (assumed from Unity's documented `-logFile -`; not tested).

## systemd, layout, deploy

### Takeaway
One service unit for the lobby; children are in the same cgroup so `KillMode=control-group` ends them when the service stops. Everything below is **untested** (no systemd in sandbox) but uses standard documented directives.

### Cited Findings
- None fetched this session for systemd directives (systemd documentation is not in the sources I could read); written from standard systemd.exec/systemd.kill semantics. Mark as untested.

### Inferences
Layout (owner `maze:maze`, web files readable by nginx):
```
/opt/maze/web/      WebGL build (index.html, Build/*.br, TemplateData/)   root:www-data 0755/0644
/opt/maze/server/   Linux server build (MazeServer.x86_64 + _Data/)       maze:maze 0755, binary chmod +x
/opt/maze/lobby/    package.json, src/, node_modules/                     maze:maze
/etc/maze/lobby.env secrets (TOKEN_SECRET, INTERNAL_KEY, ...)             root:maze 0640
```
**/etc/systemd/system/maze-lobby.service**
```ini
[Unit]
Description=Maze lobby and room supervisor
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=maze
Group=maze
WorkingDirectory=/opt/maze/lobby
EnvironmentFile=/etc/maze/lobby.env
Environment=NODE_ENV=production
ExecStart=/usr/bin/node src/server.mjs
Restart=on-failure
RestartSec=2

# children (Unity servers) live in this unit's cgroup and die with it
KillMode=control-group
KillSignal=SIGTERM
TimeoutStopSec=15
RuntimeDirectory=maze-lobby
RuntimeDirectoryMode=0750

# limits: lobby + N room processes + sockets
LimitNOFILE=8192
TasksMax=512
MemoryMax=1700M
MemoryHigh=1500M

# hardening
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
PrivateTmp=true
PrivateDevices=true
ProtectKernelTunables=true
ProtectKernelModules=true
ProtectControlGroups=true
RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX
RestrictNamespaces=true
LockPersonality=true
SystemCallArchitectures=native
ReadWritePaths=/opt/maze/server
# Unity needs writable $HOME-like dir for PlayerPrefs/cache: its cwd /opt/maze/server above

# logs go to journald (stdout/stderr); view with: journalctl -u maze-lobby -f
StandardOutput=journal
StandardError=journal
SyslogIdentifier=maze-lobby

[Install]
WantedBy=multi-user.target
```
`/etc/maze/lobby.env`:
```
LOBBY_PORT=3000
SERVER_BIN=/opt/maze/server/MazeServer.x86_64
SERVER_CWD=/opt/maze/server
PORT_MIN=7000
PORT_MAX=7099
MAX_ROOMS=8
PUBLIC_HOST=maze.example.duckdns.org
PUBLIC_PORT=443
PUBLIC_TLS=true
TOKEN_SECRET=<openssl rand -hex 32>
INTERNAL_KEY=<openssl rand -hex 16>
```
Journald rotation: `/etc/systemd/journald.conf.d/maze.conf` with `[Journal]` `SystemMaxUse=200M` `MaxRetentionSec=1week`. nginx logs rotate via the stock logrotate file. For a hard cap on Unity logs use `-logFile -` (journald) instead of a file.

**deploy.sh (run from the dev box; untested)**
```bash
#!/usr/bin/env bash
set -euo pipefail
HOST=${1:?user@host}
rsync -az --delete WebGL-build/   "$HOST:/tmp/maze-web/"
rsync -az --delete LinuxServer-build/ "$HOST:/tmp/maze-server/"
rsync -az --delete --exclude node_modules lobby/ "$HOST:/tmp/maze-lobby/"
ssh "$HOST" 'set -e
  sudo rsync -a --delete /tmp/maze-web/    /opt/maze/web/
  sudo rsync -a --delete /tmp/maze-server/ /opt/maze/server/
  sudo rsync -a --delete --exclude node_modules /tmp/maze-lobby/ /opt/maze/lobby/
  sudo chown -R maze:maze /opt/maze/server /opt/maze/lobby
  sudo chown -R root:www-data /opt/maze/web && sudo find /opt/maze/web -type d -exec chmod 755 {} + && sudo find /opt/maze/web -type f -exec chmod 644 {} +
  sudo chmod +x /opt/maze/server/MazeServer.x86_64
  (cd /opt/maze/lobby && sudo -u maze npm ci --omit=dev)
  sudo nginx -t && sudo systemctl reload nginx
  sudo systemctl restart maze-lobby'
```
Restarting the lobby kills running rooms (acceptable for a prototype; drain first otherwise). First-time setup: `useradd -r -m -d /opt/maze maze` is not needed with `User=maze` if the account exists: `sudo useradd --system --home /opt/maze --shell /usr/sbin/nologin maze`.

### Gaps
- None of these units or scripts were run.

## Docker alternative, sizing, security

### Takeaway
Skip Docker for the prototype (systemd cgroups already give supervision); resource numbers are not reliably documented, so measure on the VM. Security essentials: only 22/80/443 exposed, room ports bound to loopback.

### Cited Findings
- Reported headless Linux Unity memory is anecdotal and scene dependent: ~130 MB for a very simple terrain game, 800 MB on a heavier one, "-batchmode -nographics only reduces 100 MB", and an unverified "around 10 mb" claim. Unity profiler staff noted 8 MB per thread stack with ~2.5 threads per core by default, mitigated by `ulimit -s 1024` or fewer threads — [Unity forum "Headless server"](https://discussions.unity.com/t/headless-server/765571), [Issue Tracker](https://issuetracker.unity3d.com/issues/memory-profiler-captures-unknown-memory-when-capturing-in-a-headless-build) [search; I could not open the threads]
- Unity: desktop headless mode (`-batchmode -nographics`) "doesn't perform any optimizations for running a build as a dedicated server"; the Dedicated Server build target is optimised for memory and CPU — [Unity 6.3 manual](https://docs.unity3d.com/Manual/desktop-headless-mode.html) [search]
- A Linux headless server CPU usage increasing over time is a tracked Unity issue — [Issue Tracker](https://issuetracker.unity3d.com/issues/linux-headless-server-cpu-usage-increases-over-time) [search]

### Inferences
- **Sizing (estimate, not measured):** plan 100-250 MB RSS per small maze room with the Dedicated Server target, so on 2 GB with ~400 MB for OS, nginx and lobby, 6-12 rooms fit; the lobby default `MAX_ROOMS=8` and `MemoryMax=1700M` reflect that. Measure with `ps -o rss= -p <pid>` after 2 players join and set `MAX_ROOMS` from the real number. CPU: set the server `Application.targetFrameRate` (e.g. 30-60) because batchmode loops otherwise spin a core; this is the main CPU risk for 8 rooms on a 1-2 vCPU VM.
- **Docker (optional):** one `Dockerfile` for the lobby (`node:22-slim`) and one for the server (`debian:bookworm-slim` with the Linux build) with the lobby spawning containers via the docker socket would hand it root-equivalent power, so do not. A compose file running lobby+nginx only is fine; keep rooms as child processes.
- **Firewall (ufw):** `ufw default deny incoming; ufw allow 22/tcp; ufw allow 80/tcp; ufw allow 443/tcp; ufw enable`. Room ports 7000-7099 and lobby 3000 stay on loopback (the lobby binds 127.0.0.1; the Unity server listens on its `ServerListenAddress`, which defaults to loopback when empty, per UnityTransport source) — never open them. Oracle Cloud also needs the VCN security list opened for 80/443.
- **Abuse limits:** nginx `limit_conn`/`limit_req` (above), lobby per-route rate limits, `MAX_ROOMS`, short join-token TTL (60 s), HMAC constant-time compare, per-room player cap enforced in ConnectionApproval, server-side NGO payload size limits, `limit_conn perip 12` on the room path. A small VM cannot absorb a real DDoS; a free Cloudflare proxy in front would not carry arbitrary-port traffic and the WebSocket path through it is untested here.
- **fail2ban:** jail `sshd` (default) and an `nginx-limit-req` jail reading `/var/log/nginx/error.log` ("limiting requests" lines), `bantime = 1h`, `maxretry = 10`; use SSH keys only and `PasswordAuthentication no`.

### Gaps
- No measured per-room memory or CPU for this game; measure on the target VM.
- Whether 8 concurrent NGO servers saturate 1 Oracle Ampere A1 vCPU depends on tick rate and physics.

## Recommendation and next steps

### Takeaway
**Stack: nginx (TLS, static, `/r/<port>` regex proxy) + Node 22 / Fastify 5 lobby + one NGO Linux Dedicated Server process per room + systemd; certificates via certbot (HTTP-01 on a public name, DNS-01 via certbot-dns-duckdns otherwise).** Caddy is a fine alternative if you accept an xcaddy build for DuckDNS or want automatic certs on a public name; it is not better for routing here.

### Cited Findings
- See the sections above; the decisive fact is NGO 2.11.0 `WebSocketPath` ([CHANGELOG](https://raw.githubusercontent.com/Unity-Technologies/com.unity.netcode.gameobjects/develop-2.0.0/com.unity.netcode.gameobjects/CHANGELOG.md)).

### Inferences
Order of work: (1) Spike: WebGL client with `WebSocketPath="/r/7000"` against nginx, one fake or real server; confirm `GET /r/7000` in the access log and a completed NGO connection through wss (also settles the PlayFab "proxy drops frames" worry). (2) Run `nginx -t`/`systemctl` files on the VM. (3) Measure RSS and CPU per room. (4) Fall back to the port-range generator or `ws-router.mjs` only if step 1 fails.

### Gaps
- Items marked untested above remain unrun: all nginx/Caddy/systemd/deploy files, the C# snippets, and the real Unity binary under the lobby. Source-reading was limited to GitHub raw files; docs.unity3d.com, Unity Discussions, Mirror docs, DuckDNS and Caddy docs were only seen through search summaries.
