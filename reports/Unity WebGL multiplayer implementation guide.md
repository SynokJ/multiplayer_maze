# Route Unity WebGL players through one WSS port

**The free stack works on paper and its one risky assumption is now checkable: NGO 2.11.0 (19 March 2026) added `UnityTransport.ConnectionData.WebSocketPath`, so a browser client can request `wss://HOST/r/<port>` and nginx can rewrite that to `ws://127.0.0.1:<port>/` for the right room process.** This overturns the earlier conclusion in this project (`reports/Free Unity WebGL multiplayer workflow.md` and `docs/WORKFLOW.md`) that path routing was "probably impossible"; that conclusion was a gap in the evidence, not a finding. The implementation is: a VirtualBox Ubuntu Server VM; a free DuckDNS name with a Let's Encrypt certificate issued by DNS-01 (so it works behind NAT or on a private LAN address with no purchased domain); nginx terminating TLS and serving the Unity WebGL build; a Node 22 / Fastify lobby that spawns one Unity Linux Dedicated Server per room on loopback ports 7000-7099; and systemd supervising the lobby (rooms are children in the same cgroup). The Node lobby was actually run (with a stand-in room server) and its create, list, join, validation, reaper and SIGTERM behaviours passed. **Everything else, including all nginx, Caddy, systemd, C# and VirtualBox material, is UNTESTED**: no Unity Editor, VirtualBox, nginx, Caddy or systemd existed in the research sandbox, and most vendor documentation hosts were unreachable, so many "docs" claims are known only through search summaries. Three claims are load-bearing and unproven until a 10-minute spike passes (Chapter 6): that the WebGL JavaScript WebSocket plugin really puts `WebSocketPath` on the wire, that a TLS-terminating proxy is transparent to Unity Transport frames (one third-party issue claims otherwise), and that the client/server encryption asymmetry (`UseEncryption = true` on the browser, `false` on the server) behaves as expected. A port-range fallback and a Node upgrade router are included, both ready to use if the spike fails.

---

## How to read this guide

Every code block, command and config carries one of three labels. The label states how much evidence stands behind it, and it is deliberately conservative.

| Label | Meaning | Examples in this guide |
|---|---|---|
| **VERIFIED** | The fact or API name was read in primary source text (GitHub raw files such as the NGO repository, the Public Suffix List, a plugin README, PyPI page). This does not mean the code compiled or ran. | NGO API names, `WebSocketPath`, `caddy-dns/duckdns` options, certbot-dns-duckdns command |
| **TESTED** | The code was actually executed in the research sandbox (Node 22.22.0). | The Node lobby, `ws-router.mjs`, `fake-room-server.mjs` |
| **UNTESTED** | Authored from standard practice or search summaries and not executed. Treat as a strong draft. | nginx, Caddy, systemd, VBoxManage, C# scripts, deploy script, CI |

Two further conventions. First, "docs say" claims taken from search-result summaries rather than a fetched page are marked **(summary)**. Second, each chapter opens with a pre-flight checklist: tick every box before starting, and run the "proof" line at the end of the chapter before moving on. Placeholders to replace: `maze.example.duckdns.org` (your DuckDNS name), `maze` (VM user and service account), `you@example.com`.

The project assumptions come from the earlier workflow: Unity 6 (the notes use 6000.6.6f1 but could not verify that specific editor against NGO's declared minimum of 6000.0), Netcode for GameObjects 2.x over Unity Transport WebSockets, a headless Linux server per room, and a custom lobby instead of Unity Relay (Relay is capped at 50 average monthly CCU free, then paid) ([Unity support](https://support.unity.com/hc/en-us/articles/4410136449812-How-is-the-Relay-Service-Priced)).

---

## Chapter 0. Corrections to earlier outputs

| Earlier statement (where) | Status now | Why |
|---|---|---|
| "Unity's WebSocket client probably cannot send a path; path routing is expected to fail for NGO" (`docs/WORKFLOW.md` lines 15, 23, 72) | **Overturned (pending spike)** | NGO CHANGELOG 2.11.0 (2026-03-19): "Added a `WebSocketPath` field to `UnityTransport.ConnectionData` ... controls the path clients will connect to and servers/hosts will listen on when using WebSockets (#3901)" ([NGO CHANGELOG](https://raw.githubusercontent.com/Unity-Technologies/com.unity.netcode.gameobjects/develop-2.0.0/com.unity.netcode.gameobjects/CHANGELOG.md)). Underneath, Unity Transport has taken a path via `WithWebSocketParameters` since 2.2.0 (2023-12-01) (summary of the [Transport release notes](https://mp-docs.dl.it.unity3d.com/transport/2.0.0/release-notes/transport-2-0-0/)). |
| "Node gateway that pipes `/room/<id>` is the simpler option" (earlier report) | **Demoted to fallback** | nginx can derive the port from the URL with a regex `location` and a variable `proxy_pass`, so no per-room Node hop and no reload are needed. The Node router remains as plan C. |
| Earlier Unity forum claim "NGO has no path option; you need a custom `INetworkStreamDriverConstructor`" ([thread](https://discussions.unity.com/t/networkmanager-how-to-set-path-of-unitytransport-websocket-tcp/1594491), summary) | **Superseded** by NGO 2.11.0 | The workaround is only needed on NGO below 2.11. |
| "duckdns.org public-suffix status unconfirmed; rate limits may be shared" | **Resolved** | `duckdns.org` is on the Public Suffix List (entry "DuckDNS") ([PSL raw file](https://raw.githubusercontent.com/publicsuffix/list/master/public_suffix_list.dat)), so each `name.duckdns.org` is its own registered domain for Let's Encrypt limits (mechanism: [LE community](https://community.letsencrypt.org/t/rate-limits-and-ddns-subdomains/83200)). By contrast `sslip.io` was not found in the PSL and users hit shared-domain limits ([OpenVidu thread](https://openvidu.discourse.group/t/deployment-without-domain/5474)). |
| "IP certificates validate only by HTTP-01 or TLS-ALPN-01" (`WORKFLOW.md`) | **Supported, secondary sources** | [Caddy community](https://caddy.community/t/shortlived-certificates-from-lets-encrypt/33399), [StackScale](https://www.stackscale.com/blog/lets-encrypt-opens-the-door-to-https-by-ip/). The Let's Encrypt page itself was not read. |
| Old design assumed `SetConnectionData` then path | **New gotcha** | `SetConnectionData(...)` assigns a fresh `ConnectionAddressData` and does not copy `WebSocketPath`, so set the path after calling it ([UnityTransport.cs](https://raw.githubusercontent.com/Unity-Technologies/com.unity.netcode.gameobjects/develop-2.0.0/com.unity.netcode.gameobjects/Runtime/Transports/UTP/UnityTransport.cs)). This is my reading of the source, not a compiled test. |
| Earlier join-token sketch passed `-token` on the command line | **Changed** | argv is visible in `ps`. The lobby passes a shared HMAC secret to the room through the `ROOM_SECRET` environment variable, and per-player tokens travel in the NGO connection-approval payload. |

What did not change: a WebGL build cannot listen (UnityTransport throws "WebGL as a server is not supported by Unity Transport, outside the Editor") ([UnityTransport.cs](https://raw.githubusercontent.com/Unity-Technologies/com.unity.netcode.gameobjects/develop-2.0.0/com.unity.netcode.gameobjects/Runtime/Transports/UTP/UnityTransport.cs)), so "client as host" must be implemented as a server-side process owning the simulation, with the first joiner holding only lobby privileges.

---

## Chapter 1. Architecture and decisions

**Pre-flight checklist**
- [ ] You accept a server-authoritative design (headless Linux process per room; browsers are clients only).
- [ ] You accept Unity Personal's revenue ceiling (US$200,000 trailing 12 months) ([Unity pricing updates](https://unity.com/pricing-updates)).
- [ ] You can run an Ubuntu VM with at least 2 vCPU, 2-4 GB RAM, 20 GB disk.
- [ ] You will run the spike (Chapter 6) before investing in game logic.

### 1.1 The shape

The browser sees exactly one origin on port 443. Static files, the lobby REST API and every game room share it.

```
Browser (WebGL)
  |  https://HOST/                     -> nginx static   /opt/maze/web
  |  https://HOST/api/rooms            -> nginx -> Node lobby 127.0.0.1:3000
  |  wss://HOST/r/7003                 -> nginx rewrite -> ws://127.0.0.1:7003/   (Unity Linux server, room ABCDE)
VM:  nginx :80/:443   lobby :3000 (loopback)   rooms :7000-7099 (loopback)
```

Why this shape: rooms bind to loopback and never face the internet, so firewall, VirtualBox forwards and TLS collapse to ports 22/80/443. Same-origin hosting removes CORS, which is a real WebGL trap for `UnityWebRequest` ([Unity docs](https://docs.unity3d.com/ja//Manual/webgl-networking.html)).

### 1.2 Decision table

| Decision | Choice | Alternative | Reason / evidence |
|---|---|---|---|
| Netcode | NGO 2.13.3 + Unity Transport 2.6.0 | Mirror (SimpleWebTransport), FishNet (Bayou), Colyseus | NGO 2.13.3 requires Unity 6000.0+ and `com.unity.transport` 2.6.0 ([package.json at v2.13.3](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/blob/v2.13.3/com.unity.netcode.gameobjects/package.json)). Mirror's `ClientConnect(string hostname)` has no path setting (a `ClientConnect(Uri)` overload exists), so Mirror is not better for path routing ([SimpleWebTransport.cs](https://raw.githubusercontent.com/MirrorNetworking/Mirror/master/Assets/Mirror/Transports/SimpleWeb/SimpleWebTransport.cs)). |
| TLS termination | Proxy (nginx) | Unity server terminates (`SetServerSecrets`) | Unity's manual says that without a certificate on the game server you need Relay or a TLS-terminating reverse proxy (summary of [Transport 2.5 manual](https://docs.unity3d.com/Packages/com.unity.transport@2.5/manual/websockets.html)); a forum thread found ticking "Use Encryption" alone insufficient ([Unity Discussions](https://discussions.unity.com/t/issues-setting-up-encrypted-websocket-connection-with-unity-transport/1620104)). |
| Proxy | nginx | Caddy | Port from URL regex needs no reload; fine control of `Content-Encoding`. Caddy is easier for automatic certs but DuckDNS DNS-01 needs a custom `xcaddy` build. |
| Certificates | certbot + `certbot-dns-duckdns` (DNS-01) | acme.sh, Caddy | Works behind NAT and on a private A record. |
| Room processes | One Unity process per room, child of lobby | Many rooms in one process | NGO has one `NetworkManager` per process; no source found for multi-session hosting. |
| Supervision | systemd, `KillMode=control-group` | Docker | Docker socket access would hand the lobby root-equivalent power. |

### 1.3 What NGO's WebSocket transport can and cannot do

Unity states that UTP WebSocket support is "not intended as a general-purpose WebSocket library" and only connects UTP clients to UTP servers (summary of [Transport 2.3 manual](https://docs.unity3d.com/Packages/com.unity.transport@2.3/manual/websockets.html)). Consequences: a plain Node `ws` bot cannot play the game, and a Node relay cannot speak to an NGO client. A TLS-terminating proxy that forwards frames unchanged should be transparent, but one third-party issue says a WebSocket proxy "completes the upgrade but the Unity Transport server discards the data" ([PlayFab issue #9](https://github.com/PlayFab/MultiplayerServerSecureWebsocket/issues/9), one reporter's diagnosis, no maintainer reply). The spike settles this.

**Proof for Chapter 1:** you can draw the diagram in 1.1 from memory and name the three unproven assumptions.

---

## Chapter 2. VirtualBox VM and free HTTPS/WSS certificates

**Pre-flight checklist**
- [ ] VirtualBox 7.x installed on the host; hardware virtualization enabled in BIOS.
- [ ] Ubuntu Server ISO downloaded; SHA256 checked against the published `SHA256SUMS`.
- [ ] A DuckDNS account (sign in with an identity provider) and a subdomain created; token copied and stored in a password manager, never in a repo.
- [ ] You know whether the VM will be NAT or bridged (table 2.3) and whether it is reachable from the public internet (almost certainly not).
- [ ] Host firewall will allow the forwarded or bridged ports.

### 2.1 Which Ubuntu

Ubuntu Server 26.04 LTS ("Resolute Raccoon") was released 23 April 2026 and is supported to April 2031; server minimums are about 1.5 GB RAM and 4-5 GB disk, unchanged from 24.04 ([release notes](https://documentation.ubuntu.com/release-notes/26.04/), [Linuxiac](https://linuxiac.com/ubuntu-26-04-lts-raises-desktop-minimum-ram-requirement/), summaries). If `VBoxManage unattended detect` does not recognize the ISO, use 24.04 LTS or the GUI installer. Whether unattended install supports 26.04 was not confirmed. Guest Additions are not needed for a headless SSH-managed server (general knowledge).

### 2.2 Create the VM from the command line (UNTESTED)

Flag names are standard VBoxManage 7.x usage but the Oracle manual could not be fetched. `unattended install` takes `--iso` (required), `--user`, `--password`, `--hostname`, `--install-additions`, `--start-vm` (summary of [Oracle docs](https://docs.oracle.com/en/virtualization/virtualbox/6.0/user/vboxmanage-unattended.html)).

```bash
# UNTESTED. On the host. Windows: use "C:\Program Files\Oracle\VirtualBox\VBoxManage.exe"
ISO=ubuntu-26.04-live-server-amd64.iso
VM=maze-vm
VBoxManage unattended detect --iso="$ISO"          # check the guest OS is recognised first
VBoxManage createvm --name $VM --ostype Ubuntu_64 --register
VBoxManage modifyvm $VM --memory 4096 --cpus 2 --ioapic on --boot1 dvd --boot2 disk \
  --nic1 nat --natpf1 "ssh,tcp,,2222,,22" \
  --natpf1 "http,tcp,,8080,,80" --natpf1 "https,tcp,,8443,,443"
VBoxManage createmedium disk --filename "$HOME/VirtualBox VMs/$VM/$VM.vdi" --size 20480
VBoxManage storagectl $VM --name SATA --add sata --controller IntelAhci
VBoxManage storageattach $VM --storagectl SATA --port 0 --device 0 --type hdd \
  --medium "$HOME/VirtualBox VMs/$VM/$VM.vdi"
VBoxManage storagectl $VM --name IDE --add ide
VBoxManage unattended install $VM --iso="$ISO" --user=maze --password='CHANGE_ME' \
  --hostname=maze-vm.local --start-vm=headless
# If unattended install is unsupported: VBoxManage startvm $VM  and use the GUI installer
# (tick "Install OpenSSH server" in the installer).
ssh -p 2222 maze@127.0.0.1          # NAT;   bridged: ssh maze@<vm-ip>
ssh-copy-id -p 2222 maze@127.0.0.1
```

GUI route: New, name and ISO, optionally "Skip Unattended Installation", 4096 MB, 2 CPUs, 20 GB, then Settings, Network.

### 2.3 NAT versus bridged

| Mode | VM reachable from | Needs | Good for | Caveats |
|---|---|---|---|---|
| NAT | Host only, unless forwards exist | Per-port forward rules; host ports under 1024 may need privileges, so use 8080/8443 | Solo dev on one machine | LAN peers connect to the host IP; open the host firewall |
| Bridged | Whole LAN as its own host | Exact adapter name | Phones and second PCs | Wi-Fi bridging can be unreliable (general experience, unverified); exposed to the LAN |
| Host-only | Host and other VMs | none | Isolated tests | No phone testing |

Sources: [Cleverence manual summary](https://www.cleverence.com/articles/oracle-documentation/6-5-bridged-networking-oracle-vm-virtualbox-3742/), [DevGenius](https://blog.devgenius.io/choosing-the-right-virtualbox-network-adapter-nat-host-only-or-bridged-188003a260de).

```bash
# UNTESTED. NAT forwards (name,proto,hostip,hostport,guestip,guestport)
VBoxManage modifyvm maze-vm --natpf1 "https,tcp,,443,,443"     # VM powered off
VBoxManage controlvm maze-vm natpf1 "extra,tcp,,9000,,9000"    # while running
VBoxManage modifyvm maze-vm --natpf1 delete ssh
# Bridged:
VBoxManage list bridgedifs
VBoxManage modifyvm maze-vm --nic1 bridged --bridgeadapter1 "<exact adapter name>"
```

With the path-routing design only 22, 80 and 443 need forwarding. Only the port-range fallback (Chapter 5.9) needs a 100-port loop: `for p in $(seq 8000 8099); do VBoxManage modifyvm maze-vm --natpf1 "ws$p,tcp,,$p,,$p"; done` (no range syntax is known to exist; unverified).

### 2.4 Free domain with DuckDNS, and the NAT trick

A DuckDNS A record may point at a private address. Let's Encrypt validates DNS-01 by querying only the TXT record, so the certificate is real and browser-trusted even though the VM is not internet-reachable. This is the one sourced route to a trusted certificate for a non-public VM ([Caddy community](https://caddy.community/t/dns-challenge-with-duckdns/14994), [caddy-dns/duckdns](https://github.com/caddy-dns/duckdns)).

The DuckDNS update API takes `domains`, `token`, optional `ip`, `ipv6`, `verbose`, `clear`, and replies `OK` or `KO`; an omitted `ip` means the detected public IPv4, but any IP including a private one can be supplied; TXT uses `txt=`, and one TXT record serves the domain and all its sub-subdomains, so only one TXT exists at a time ([DuckDNS spec](https://www.duckdns.org/spec.jsp), summary).

```bash
# UNTESTED, standard DuckDNS API. Point the name at the bridged VM's LAN IP.
TOKEN=...   # from the top of the duckdns.org page after login
curl -s "https://www.duckdns.org/update?domains=maze&token=$TOKEN&ip=192.168.1.50"   # expect OK
dig +short maze.duckdns.org                                                          # expect 192.168.1.50
```

Home routers with DNS-rebind protection may drop private answers; the fix is a router exception or a hosts-file entry (inference). Not confirmed by any source: the free-account subdomain limit (commonly said to be 5). No-IP free hostnames need confirmation every 30 days or so (23 active days, 7-day confirmation window, 14-day redemption), so prefer DuckDNS for long-lived development ([No-IP support](https://www.noip.com/support/?p=2887)).

### 2.5 Issue the certificate: three tools

Use `--staging` until the flow works; staging certificates are untrusted by design (Let's Encrypt, [certbot-dns-duckdns on PyPI](https://pypi.org/project/certbot-dns-duckdns/)). Let's Encrypt limits authorization failures to 5 per identifier per account per hour, and 5 duplicate certificates per 7 days ([LE rate limits](https://letsencrypt.org/docs/rate-limits/), [LE community](https://community.letsencrypt.org/t/understanding-certificate-limits/140807), summaries). Sources disagree on the per-registered-domain limit (20 versus 50), so check the live rate-limit page.

**Option A (recommended with nginx): certbot with the DuckDNS plugin.** The plugin command and credentials-file format are VERIFIED from PyPI; the venv install steps are UNTESTED.

```bash
# plugin usage VERIFIED; packaging steps UNTESTED
sudo apt install -y python3-venv
sudo python3 -m venv /opt/certbot
sudo /opt/certbot/bin/pip install certbot certbot-dns-duckdns          # needs certbot >= 1.18
echo 'dns_duckdns_token=TOKEN' | sudo tee /root/duckdns.ini && sudo chmod 600 /root/duckdns.ini
sudo /opt/certbot/bin/certbot certonly --non-interactive --agree-tos --email you@example.com \
  --preferred-challenges dns --authenticator dns-duckdns \
  --dns-duckdns-credentials /root/duckdns.ini --dns-duckdns-propagation-seconds 60 \
  -d maze.duckdns.org --staging          # drop --staging for the real certificate
# renewal hook and schedule
printf '#!/bin/sh\nsystemctl reload nginx\n' | sudo tee /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh
sudo chmod +x /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh
echo '0 3 * * * root /opt/certbot/bin/certbot renew -q' | sudo tee /etc/cron.d/certbot-renew
```

Plugin facts from PyPI: one DuckDNS domain per call; default propagation 30 s (examples use 60); wildcard `-d "*.maze.duckdns.org"` is supported; install as root if certbot runs as root; the snap certbot needs the snap plugin. Wildcard and apex cannot be validated in one run because DuckDNS holds a single TXT; request them separately ([LE community](https://community.letsencrypt.org/t/unable-to-create-wildcard-certificate-on-duckdns/190287)).

**Option B: acme.sh.** The hook reads the environment variable `DuckDNS_Token` and `--install-cert` with `--reloadcmd` is the documented way to place files ([dns_duckdns.sh](https://raw.githubusercontent.com/acmesh-official/acme.sh/master/dnsapi/dns_duckdns.sh), [README](https://raw.githubusercontent.com/acmesh-official/acme.sh/master/README.md)). UNTESTED assembly:

```bash
curl https://get.acme.sh | sh -s email=you@example.com; source ~/.bashrc
acme.sh --set-default-ca --server letsencrypt        # some versions default to ZeroSSL (unverified)
export DuckDNS_Token="TOKEN"
acme.sh --issue --dns dns_duckdns -d maze.duckdns.org --staging     # remove --staging when it works
sudo mkdir -p /etc/ssl/maze
sudo acme.sh --install-cert -d maze.duckdns.org \
  --fullchain-file /etc/ssl/maze/fullchain.pem --key-file /etc/ssl/maze/privkey.pem \
  --reloadcmd "systemctl reload nginx"
```

**Option C: Caddy with the DuckDNS module.** Caddy issues and renews automatically, but needs a custom build: `xcaddy build --with github.com/caddy-dns/duckdns`; without it you get "module not registered: dns.providers.duckdns"; check with `caddy list-modules`. The module name is `dns.providers.duckdns`, Caddyfile `tls { dns duckdns {env.DUCKDNS_API_TOKEN} }`, options `override_domain` and `resolver` (default 8.8.8.8:53) ([caddy-dns/duckdns README](https://github.com/caddy-dns/duckdns), VERIFIED; build advice from [Caddy community](https://caddy.community/t/best-way-to-install-and-maintain-caddy-with-dns-module/30701), summary). A 2025 log showed module v0.4.0, and bug reports exist of the plugin using the wrong zone ([Caddy community](https://caddy.community/t/dns-challenge-on-duck-dns-fails-with-connection-refused/21913), summary).

```bash
# UNTESTED
sudo apt install -y golang-go
go install github.com/caddyserver/xcaddy/cmd/xcaddy@latest
~/go/bin/xcaddy build --with github.com/caddy-dns/duckdns
sudo apt install -y caddy && sudo systemctl stop caddy
sudo cp caddy /usr/bin/caddy && caddy list-modules | grep duckdns
sudo setcap cap_net_bind_service=+ep /usr/bin/caddy       # if the copied binary cannot bind 80/443
echo 'DUCKDNS_API_TOKEN=xxxx' | sudo tee /etc/caddy/duckdns.env && sudo chmod 600 /etc/caddy/duckdns.env
sudo systemctl edit caddy      # add:  [Service]  EnvironmentFile=/etc/caddy/duckdns.env
```

### 2.6 Let's Encrypt IP certificates: when they apply

IP-address and short-lived certificates went generally available on 15 January 2026; IP certificates are always the 160-hour "shortlived" profile, for IPv4 or IPv6, and validate by HTTP-01 or TLS-ALPN-01 only ([Let's Encrypt GA post](https://letsencrypt.org/2026/01/15/6day-and-ip-general-availability) (summary), [Help Net Security](https://www.helpnetsecurity.com/2026/01/20/lets-encrypt-6-day-tls-certificates/), [StackScale](https://www.stackscale.com/blog/lets-encrypt-opens-the-door-to-https-by-ip/)). They need ports 80 or 443 reachable from the public internet, so they do **not** work on a NAT or LAN VM unless you forward the ports at the router and the ISP gives a reachable public IPv4 (no CGNAT); they do work on a public free VM such as Oracle Cloud. Certbot supports them from 5.3 (`--ip-address`) and webroot IPs from 5.4; the official command is below ([Let's Encrypt](https://letsencrypt.org/2026/03/11/shorter-certs-certbot.html), [EFF](https://eff.org/deeplinks/2026/03/certbot-and-lets-encrypt-now-support-ip-address-certificates), summaries). The acme.sh README has no mention of IP certificates (VERIFIED absence).

```bash
# UNTESTED here; command text from the Let's Encrypt/EFF announcement (summary). Public IP only.
sudo certbot certonly --staging --preferred-profile shortlived --webroot --webroot-path /var/www/acme --ip-address 203.0.113.10
```

```caddyfile
# UNTESTED. Caddy profile syntax from Caddy community; the Dec 2025 caddy#7399 issue predates GA and is probably obsolete
https://203.0.113.10 {
    tls { issuer acme { profile shortlived } }
    reverse_proxy 127.0.0.1:7777
}
```

Renewal must run at least daily because of the 160-hour life; Caddy reportedly renews about every 2 days.

### 2.7 Local-only options (no public name)

Caddy `tls internal` or mkcert give certificates from a local CA, which every client device must trust. Self-signed certificates do not work in browsers by default (Transport manual, summary) and WebSocket failures on untrusted certificates are silent, so open `https://HOST` in a tab and accept it first, or install the CA. All trust procedures below are UNTESTED and unsourced.

```bash
mkcert -install && mkcert 192.168.1.50 mazevm.local            # mkcert (sudo apt install mkcert libnss3-tools)
# Caddy root CA: /var/lib/caddy/.local/share/caddy/pki/authorities/local/root.crt
certutil -addstore -f ROOT root.crt                            # Windows (admin)
sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain root.crt   # macOS
```
Android: Settings, Security, Install certificate, CA certificate (Chrome accepts user CAs on Android 7+, apps may not). iOS: install the profile, then Settings, General, About, Certificate Trust Settings. Firefox has its own store (`security.enterprise_roots.enabled`).

### 2.8 Base hardening and clock (UNTESTED, standard practice)

```bash
sudo apt update && sudo apt -y upgrade
sudo ufw default deny incoming; sudo ufw default allow outgoing
sudo ufw allow OpenSSH; sudo ufw allow 80/tcp; sudo ufw allow 443/tcp
sudo ufw enable
sudo apt install -y unattended-upgrades fail2ban && sudo dpkg-reconfigure -plow unattended-upgrades
timedatectl status          # want: System clock synchronized: yes (suspended VMs drift; TLS and ACME need correct time)
sudo useradd --system --home /opt/maze --shell /usr/sbin/nologin maze
```

**Proof for Chapter 2:** `dig +short maze.duckdns.org` returns the expected IP; `sudo openssl x509 -in /etc/letsencrypt/live/maze.duckdns.org/fullchain.pem -noout -issuer -dates` shows the (staging, then production) issuer and dates; `ssh` key login works.

