# VirtualBox Ubuntu VM with free, browser-trusted Let's Encrypt certificates (HTTPS + WSS)

Method and caveat: WebFetch reached only github.com, raw.githubusercontent.com and pypi.org. letsencrypt.org, virtualbox.org, docs.oracle.com and eff.org failed with DNS errors. Everything attributed to Let's Encrypt, Oracle or Caddy docs below therefore comes from WebSearch result summaries. Items marked "Verified" were read from a fetched primary page or file. Command blocks under "Runnable recipe" are assembled from the cited facts plus general Linux/VirtualBox/Caddy knowledge and were NOT executed (no VirtualBox here). They are flagged "Unverified syntax" where relevant. Today is 2026-10-26.

## 1. Corrections and confirmations against prior notes (wss_tls_hosting.md, docs/WORKFLOW.md)

### Takeaway
The prior design (VM bridged or NAT, DuckDNS + Caddy DNS-01, IP certs only on a public IP) holds. IP-cert validation limits are now backed by a source; the Caddy IP-cert syntax, VirtualBox flag syntax and DuckDNS public-suffix status were previously unconfirmed and are now covered.

### Cited Findings
- duckdns.org IS on the Public Suffix List (entry "DuckDNS", submitted by Richard Harper). Verified from the raw PSL file. This corrects/closes the gap in the earlier notes: each `name.duckdns.org` counts as its own registered domain for Let's Encrypt, so the per-registered-domain limits are per your subdomain, not shared with all DuckDNS users (this is the mechanism Let's Encrypt applies; contrast sslip.io, which was NOT found in the PSL grep and which previous notes show hits shared limits) — [PSL raw file](https://raw.githubusercontent.com/publicsuffix/list/master/public_suffix_list.dat); mechanism: [LE community](https://community.letsencrypt.org/t/rate-limits-and-ddns-subdomains/83200)
- The same PSL grep shows these also listed: dynv6.net, dynuddns.net, No-IP domains (ddns.net, hopto.org, no-ip.org, etc.), ts.net. Searching for afraid.org/mooo/sslip/nip.io returned no matches in my grep pattern (so those are not confirmed listed) — PSL raw file above. Note: FreeDNS shared domains were discussed in [LE community thread](https://community.letsencrypt.org/t/exempt-freedns-domains-from-the-rate-limit/7629) (older; contents not retrieved).
- WORKFLOW.md claim "IP certs validate only by HTTP-01 or TLS-ALPN-01": now supported by a search summary stating validation is restricted to http-01 and tls-alpn-01 because DNS-01 cannot prove control of an IP — [Caddy community / StackScale summary](https://caddy.community/t/shortlived-certificates-from-lets-encrypt/33399), [StackScale](https://www.stackscale.com/blog/lets-encrypt-opens-the-door-to-https-by-ip/) (secondary, not the LE page itself)
- WORKFLOW.md "Caddy IP cert: probable": Caddy community shows `issuer acme { profile shortlived }` inside `tls`; a Dec 2025 Caddy GitHub issue (pre-GA) reported IP identifiers rejected even with the profile, likely obsolete — [Caddy community](https://caddy.community/t/shortlived-certificates-from-lets-encrypt/33399), [caddy#7399](https://github.com/caddyserver/caddy/issues/7399)

### Inferences
- WORKFLOW.md line 66 ("A NAT or LAN VM is not publicly reachable, so LE IP and HTTP-01 certs will not work") is correct unless port 80/443 are forwarded on the home router to the VM AND the ISP gives a reachable public IPv4 (no CGNAT). That edge case is not mentioned in prior notes.

### Gaps
- Primary LE page for IP-cert validation methods not read (fetch blocked).

## 2. VirtualBox VM creation (Ubuntu Server LTS, sizing, CLI, Guest Additions, SSH)

### Takeaway
Use Ubuntu Server 26.04 LTS (released 23 April 2026, supported to April 2031) or 24.04 LTS if a 26.04 ISO/VirtualBox unattended template misbehaves. 2 vCPU, 2-4 GB RAM, 20 GB disk is ample for Caddy + a few game servers. Guest Additions are not needed for a headless server reached via SSH.

### Cited Findings
- Ubuntu 26.04 LTS "Resolute Raccoon" released 23 April 2026, supported until April 2031 (10 years with Ubuntu Pro); server minimum ~1.5 GB RAM and 4-5 GB storage, unchanged from 24.04 (the 6 GB RAM rise is Desktop only) — [Ubuntu 26.04 release notes](https://documentation.ubuntu.com/release-notes/26.04/), [Linuxiac](https://linuxiac.com/ubuntu-26-04-lts-raises-desktop-minimum-ram-requirement/) (via search summary)
- `VBoxManage unattended install` takes `--iso` (required) and optional `--user`, `--password`, `--hostname`, `--install-additions`, `--start-vm`; `VBoxManage unattended detect --iso=...` checks guest-OS detection. Whether unattended install fully supports Ubuntu Server 26.04 was not confirmed. A user created this way may need adding to sudo — [Oracle VBoxManage unattended](https://docs.oracle.com/en/virtualization/virtualbox/6.0/user/vboxmanage-unattended.html), [Kifarunix VB7](https://kifarunix.com/perform-unattended-vm-installation-on-virtualbox-7/) (search summaries)

### Runnable recipe (Unverified syntax; standard VBoxManage 7.x usage)
```bash
# on the host (Windows: use "C:\Program Files\Oracle\VirtualBox\VBoxManage.exe")
ISO=ubuntu-26.04-live-server-amd64.iso     # download from ubuntu.com; verify SHA256SUMS
VM=maze-vm
VBoxManage createvm --name $VM --ostype Ubuntu_64 --register
VBoxManage modifyvm $VM --memory 4096 --cpus 2 --ioapic on --boot1 dvd --boot2 disk \
  --nic1 nat --natpf1 "ssh,tcp,,2222,,22" \
  --natpf1 "http,tcp,,8080,,80" --natpf1 "https,tcp,,8443,,443"
VBoxManage createmedium disk --filename "$HOME/VirtualBox VMs/$VM/$VM.vdi" --size 20480
VBoxManage storagectl $VM --name SATA --add sata --controller IntelAhci
VBoxManage storageattach $VM --storagectl SATA --port 0 --device 0 --type hdd --medium "$HOME/VirtualBox VMs/$VM/$VM.vdi"
VBoxManage storagectl $VM --name IDE --add ide
VBoxManage unattended install $VM --iso=$ISO --user=maze --password='CHANGE_ME' \
  --hostname=maze-vm.local --start-vm=headless      # add --install-additions only if you want GA
# if unattended does not support the ISO, run: VBoxManage startvm $VM  and use the GUI installer
# (GUI path: New -> name/ISO, tick "Skip Unattended Installation" if desired, 4096 MB, 2 CPU, 20 GB; Settings -> Network)
```
- Install OpenSSH during the Ubuntu installer ("Install OpenSSH server" checkbox), then `ssh -p 2222 maze@127.0.0.1` for NAT, or `ssh maze@<vm-ip>` for bridged. Copy a key: `ssh-copy-id -p 2222 maze@127.0.0.1`.
- Guest Additions: not required for SSH, networking, or serving. Only needed for shared folders/clipboard/resizing. (General knowledge; not source-verified.)

### Gaps
- Official Oracle manual could not be fetched; flag names `--natpf1`, `--nic1 bridged --bridgeadapter1`, `createmedium` are standard but unverified here. Exact 26.04 unattended-install compatibility unknown; test `unattended detect` first.

## 3. Networking mode and its consequences for validation

### Takeaway
Bridged = VM gets its own LAN IP, best for phones/other PCs testing; NAT needs forwarded ports (and host ports below 1024 may need admin/root on the host, so use 8080/8443 or forward as shown). For Let's Encrypt, DNS-01 via DuckDNS needs no inbound access, so any mode works. HTTP-01/TLS-ALPN-01 need ports 80/443 reachable from the public internet, which a home VM generally is not.

### Cited Findings
- Bridged: guest has its own MAC/IP on the LAN, reachable without port forwarding; NAT: inbound blocked unless host-port forwards exist, LAN peers use host IP; host-only: host and VMs only — [Cleverence/VirtualBox manual summary](https://www.cleverence.com/articles/oracle-documentation/6-5-bridged-networking-oracle-vm-virtualbox-3742/), [DevGenius](https://blog.devgenius.io/choosing-the-right-virtualbox-network-adapter-nat-host-only-or-bridged-188003a260de)
- Let's Encrypt limits authorization failures to 5 per identifier per account per hour; 10 accounts per IP per 3 hours — [LE rate limits](https://letsencrypt.org/docs/rate-limits/) (via search summary)

### Runnable recipe (Unverified syntax)
```bash
# NAT with forwards (rule format: name,proto,hostip,hostport,guestip,guestport); blank hostip = all host interfaces
VBoxManage modifyvm maze-vm --natpf1 "ssh,tcp,,2222,,22"
VBoxManage modifyvm maze-vm --natpf1 "http,tcp,,80,,80" --natpf1 "https,tcp,,443,,443"
VBoxManage modifyvm maze-vm --natpf1 "wsrange8000,tcp,,8000,,8000"   # no range syntax: one rule per port
for p in $(seq 8000 8019); do VBoxManage modifyvm maze-vm --natpf1 "ws$p,tcp,,$p,,$p"; done  # VM powered off
VBoxManage controlvm maze-vm natpf1 "extra,tcp,,9000,,9000"   # add while running
VBoxManage modifyvm maze-vm --natpf1 delete ssh
# Bridged alternative:
VBoxManage list bridgedifs
VBoxManage modifyvm maze-vm --nic1 bridged --bridgeadapter1 "<exact adapter name>"
```
- Open the host OS firewall for forwarded ports to accept LAN connections.

### Inferences
- VirtualBox has no port-range natpf syntax (I believe; unverified); a loop is the workaround. Host ports <1024 on Linux/macOS hosts need privileges, so prefer 8080/8443 for NAT or use bridged.
- Wi-Fi bridging can be unreliable on some adapters (general experience; unverified).
- DNS-01 works in all modes because Let's Encrypt only queries the TXT record at DuckDNS.

### Gaps
- Official natpf1 syntax and bridged-Wi-Fi limits not confirmed from Oracle manual.

## 4. Free domain: DuckDNS and alternatives; DNS-01 with a private IP

### Takeaway
A DuckDNS subdomain whose A record points at a private 192.168.x.x address can still get a real Let's Encrypt cert via DNS-01 (Let's Encrypt only checks the TXT record, never connects). DuckDNS is on the PSL, so rate limits apply per subdomain.

### Cited Findings
- DuckDNS update API: HTTPS GET with `domains`, `token`, optional `ip`, `ipv6`, `verbose`, `clear`; replies OK/KO; omitted ip = detected public IPv4; you can supply any IP (including private) — [DuckDNS spec](https://www.duckdns.org/spec.jsp) (via search summary)
- DuckDNS TXT: `https://www.duckdns.org/update?domains=...&token=...&txt=...`; one TXT record applies to the domain and all sub-subdomains (so only one TXT at a time) — same spec
- Token is shown at the top of the duckdns.org page after login — [caddy-dns/duckdns README](https://github.com/caddy-dns/duckdns) (Verified)
- Caddy docs-era community reports that DNS-01 with DuckDNS works when the A record points to a LAN IP — [Caddy community](https://caddy.community/t/dns-challenge-with-duckdns/14994) (earlier notes)
- No-IP free hostnames: 23 active days, then a 7-day confirmation window, then 14-day redemption; IP updates do not count as confirmation; paid plan avoids it — [No-IP support](https://www.noip.com/support/?p=2887)
- FreeDNS (afraid.org): only a historical LE community thread found; no current limits retrieved.
- DuckDNS free-subdomain quota (commonly cited as 5): NOT confirmed from any source I read.

### Runnable recipe (Unverified, standard DuckDNS API)
```bash
# sign in at duckdns.org (GitHub/Google etc.), create e.g. mazegame -> mazegame.duckdns.org, copy TOKEN
curl -s "https://www.duckdns.org/update?domains=mazegame&token=$TOKEN&ip=192.168.1.50"    # private LAN IP (bridged VM IP)
dig +short mazegame.duckdns.org
```
- Browsers on your LAN then resolve mazegame.duckdns.org to the VM (home routers with DNS-rebind protection may block private answers; if so, use a hosts-file entry or router exception — inference).

### Inferences
- Because PSL-listed, 50-certs-per-registered-domain/week style limits apply to your own name only; still use `--staging` while testing.
- Prefer DuckDNS over No-IP free (monthly manual confirmation) for a long-lived dev setup.

### Gaps
- DuckDNS subdomain count and any usage caps; FreeDNS current terms; exact LE rate-limit numbers (the summaries listed categories but not the figures; commonly 50 certs/registered domain/week, 5 duplicate/week, 300 new orders/account/3h, but unverified here).

## 5. Certificate tooling with exact commands

### Takeaway
Easiest: Caddy built with the duckdns module (auto-issue and auto-renew, native WebSocket proxying). Alternatives: certbot + `certbot-dns-duckdns` (Verified usage) or acme.sh `dns_duckdns` (Verified variable name).

### Cited Findings
- Caddy module name `dns.providers.duckdns`; Caddyfile `tls { dns duckdns {env.DUCKDNS_API_TOKEN} }`; options `override_domain`, `resolver` (default 8.8.8.8:53); JSON provider fields `name, api_token, override_domain, resolver` — [caddy-dns/duckdns](https://github.com/caddy-dns/duckdns) (Verified)
- Building: `xcaddy build --with github.com/caddy-dns/duckdns`; without it you get "module not registered: dns.providers.duckdns"; verify with `caddy list-modules`; `caddy add-package` relies on Caddy's build server, which had issues reported — [Caddy community 30701](https://caddy.community/t/best-way-to-install-and-maintain-caddy-with-dns-module/30701), [29998](https://caddy.community/t/building-and-running-caddy-duckdns-provider/29998) (via search summary)
- certbot plugin: `pip install certbot_dns_duckdns` (certbot >=1.18; install as root if certbot runs as root; snap certbot needs the snap plugin); credentials file line `dns_duckdns_token=<token>`; command below; wildcard `-d "*.example.duckdns.org"`; one DuckDNS domain per call; default propagation 30 s, examples 60 — [PyPI certbot-dns-duckdns](https://pypi.org/project/certbot-dns-duckdns/) (Verified)
- acme.sh: DuckDNS hook reads env `DuckDNS_Token`; install `curl https://get.acme.sh | sh -s email=my@example.com` (adds daily cron); use `--install-cert` with `--reloadcmd` rather than reading ~/.acme.sh files — [dns_duckdns.sh](https://raw.githubusercontent.com/acmesh-official/acme.sh/master/dnsapi/dns_duckdns.sh), [acme.sh README](https://raw.githubusercontent.com/acmesh-official/acme.sh/master/README.md) (Verified)
- acme.sh + DuckDNS cannot validate root and wildcard of the same domain in one run (single TXT); issue separately or use another client — [LE community](https://community.letsencrypt.org/t/unable-to-create-wildcard-certificate-on-duckdns/190287)

### Runnable recipe
(a) Caddy (Unverified assembly of verified pieces):
```bash
sudo apt install -y golang-go          # or install current Go; then:
go install github.com/caddyserver/xcaddy/cmd/xcaddy@latest
~/go/bin/xcaddy build --with github.com/caddy-dns/duckdns
sudo apt install -y caddy              # official Caddy apt repo gives systemd unit + user
sudo systemctl stop caddy; sudo cp caddy /usr/bin/caddy; caddy list-modules | grep duckdns
echo 'DUCKDNS_API_TOKEN=xxxx' | sudo tee /etc/caddy/duckdns.env   # chmod 600
sudo systemctl edit caddy        # add [Service] EnvironmentFile=/etc/caddy/duckdns.env
```
`/etc/caddy/Caddyfile`:
```
{
	# acme_ca https://acme-staging-v02.api.letsencrypt.org/directory   # uncomment while testing
	email you@example.com
}
mazegame.duckdns.org {
	tls { dns duckdns {env.DUCKDNS_API_TOKEN} }
	root * /var/www/game
	file_server
	encode            # do not double-compress pre-compressed Unity files; see Unity notes
	handle /ws* { reverse_proxy 127.0.0.1:7777 }   # Caddy proxies WebSocket upgrades automatically
}
mazegame.duckdns.org:8443 {            # extra TLS port to a game server, e.g. proxy-terminated range
	tls { dns duckdns {env.DUCKDNS_API_TOKEN} }
	reverse_proxy 127.0.0.1:7001
}
```
Then `sudo systemctl restart caddy`. Caddy renews automatically; no hook needed.

(b) certbot (command Verified from PyPI; apt/venv install steps unverified):
```bash
sudo apt install -y python3-venv; sudo python3 -m venv /opt/certbot && sudo /opt/certbot/bin/pip install certbot certbot-dns-duckdns
echo 'dns_duckdns_token=TOKEN' | sudo tee /root/duckdns.ini; sudo chmod 600 /root/duckdns.ini
sudo /opt/certbot/bin/certbot certonly --non-interactive --agree-tos --email you@example.com \
  --preferred-challenges dns --authenticator dns-duckdns \
  --dns-duckdns-credentials /root/duckdns.ini --dns-duckdns-propagation-seconds 60 \
  -d mazegame.duckdns.org --staging      # drop --staging for the real cert
# renewal + nginx reload:
echo '#!/bin/sh
systemctl reload nginx' | sudo tee /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh; sudo chmod +x /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh
# schedule: cron  0 3 * * * /opt/certbot/bin/certbot renew -q
```
nginx: `ssl_certificate /etc/letsencrypt/live/mazegame.duckdns.org/fullchain.pem; ssl_certificate_key .../privkey.pem;` plus WebSocket proxy headers `proxy_http_version 1.1; proxy_set_header Upgrade $http_upgrade; proxy_set_header Connection "upgrade";` (standard nginx, unverified here).

(c) acme.sh:
```bash
curl https://get.acme.sh | sh -s email=you@example.com; source ~/.bashrc
export DuckDNS_Token="TOKEN"
acme.sh --issue --dns dns_duckdns -d mazegame.duckdns.org --staging     # remove --staging when it works
sudo acme.sh --install-cert -d mazegame.duckdns.org \
  --fullchain-file /etc/ssl/maze/fullchain.pem --key-file /etc/ssl/maze/privkey.pem \
  --reloadcmd "systemctl reload nginx"
```
(Run `--set-default-ca --server letsencrypt` if acme.sh defaults to ZeroSSL in your version; unverified.)

### Inferences
- Caddy is the fewest-moving-parts route. Unity's own UTP can also load fullchain.pem/privkey.pem (earlier notes), reusing the certbot/acme.sh files, but renewal needs a process restart.

### Gaps
- Current xcaddy install line and Go version requirements; whether the Caddy apt package or `caddy add-package` works today.

## 6. Let's Encrypt IP-address certificates

### Takeaway
GA since 2026-01-15, mandatory 'shortlived' profile (160 h), IPv4/IPv6. Not usable on a NAT/LAN VM; only with a public IP reachable on 80 or 443.

### Cited Findings
- IP address and short-lived certs generally available from 15 Jan 2026; IP certs are always short-lived (160 hours, just over 6 days); domain short-lived certs are opt-in via the 'shortlived' profile — [Let's Encrypt GA post](https://letsencrypt.org/2026/01/15/6day-and-ip-general-availability) (via search summary), [Help Net Security](https://www.helpnetsecurity.com/2026/01/20/lets-encrypt-6-day-tls-certificates/)
- Validation limited to http-01 and tls-alpn-01 (no DNS-01) — [StackScale](https://www.stackscale.com/blog/lets-encrypt-opens-the-door-to-https-by-ip/), [Caddy community](https://caddy.community/t/shortlived-certificates-from-lets-encrypt/33399) (secondary)
- Certbot: `--preferred-profile` since 4.0; `--ip-address` new in 5.3; webroot IP support in 5.4 (released ~10 March 2026); plugins supporting IPs: `--manual`, `--standalone`, `--webroot`. Official command: `sudo certbot certonly --staging --preferred-profile shortlived --webroot --webroot-path <root> --ip-address <ip>`; certbot does not install into the web server; files in /etc/letsencrypt/live/<ip>/ — [Let's Encrypt: shorter certs certbot](https://letsencrypt.org/2026/03/11/shorter-certs-certbot.html), [EFF](https://eff.org/deeplinks/2026/03/certbot-and-lets-encrypt-now-support-ip-address-certificates) (via search summary)
- Caddy: `issuer acme { profile shortlived }` in a `tls` block; reported to renew about every 2 days of the 160 h life — [Caddy community](https://caddy.community/t/shortlived-certificates-from-lets-encrypt/33399), [StackScale](https://www.stackscale.com/blog/lets-encrypt-opens-the-door-to-https-by-ip/)
- acme.sh README (fetched) has NO mention of IP certificates or shortlived profile — [acme.sh README](https://raw.githubusercontent.com/acmesh-official/acme.sh/master/README.md) (Verified absence; support may still exist via flags like `--cert-profile`, unverified)

### Runnable recipe (Unverified for Caddy)
```
https://203.0.113.10 {
	tls { issuer acme { profile shortlived } }
	reverse_proxy 127.0.0.1:7777
}
```
Needs 80/443 inbound from the internet to the VM (e.g., Oracle Cloud VM), not a home NAT VM.

### Inferences
- Renewal cadence: with 160 h lifetime, renew daily/every ~2 days; certbot's default twice-daily timer is adequate if current.
- A browser connecting to https://IP works but Unity WebGL code and cookies work normally; Unity client `serverName` would be the IP (not tested).

### Gaps
- Exact Caddy minimum version; acme.sh flag; LE-page confirmation of validation methods; IP-cert rate limits.

## 7. Local-only alternatives; staging; rate limits

### Takeaway
For offline/LAN with no domain: Caddy `tls internal` or mkcert, but each client device must trust the local root CA. For testing against the real flow, use LE staging (untrusted cert).

### Cited Findings
- Let's Encrypt staging certs are untrusted; `--staging` in certbot; used to avoid rate limits — [PyPI certbot-dns-duckdns](https://pypi.org/project/certbot-dns-duckdns/), [EFF/LE certbot post](https://letsencrypt.org/2026/03/11/shorter-certs-certbot.html)
- Self-signed certs do not work in browsers by default (Unity docs) — [Unity Transport WebSockets](https://docs.unity3d.com/Packages/com.unity.transport@2.5/manual/websockets.html)

### Runnable recipe (Unverified; standard tooling)
```
# Caddyfile
:443 {
	tls internal
	reverse_proxy 127.0.0.1:7777
}
# root CA: /var/lib/caddy/.local/share/caddy/pki/authorities/local/root.crt  (copy to clients)
# mkcert: sudo apt install mkcert libnss3-tools; mkcert -install; mkcert 192.168.1.50 mazevm.local
```
- Trust: Windows: `certutil -addstore -f ROOT root.crt` (admin) or double-click -> Trusted Root; macOS: `sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain root.crt`; Android: Settings > Security > Install a certificate > CA certificate (apps/Chrome on Android 7+ accept user CAs in browser, not necessarily in apps); iOS: install profile, then Settings > General > About > Certificate Trust Settings, enable full trust. Firefox keeps its own store (enable `security.enterprise_roots.enabled` or import).

### Gaps
- None of the trust procedures were source-checked this session (mkcert/Caddy docs unreachable).

## 8. Firewall, hardening, time sync, updates

### Takeaway
No sources retrieved for this section; the following is standard Ubuntu practice and unverified here.

### Runnable recipe (Unverified)
```bash
sudo ufw default deny incoming; sudo ufw default allow outgoing
sudo ufw allow OpenSSH; sudo ufw allow 80/tcp; sudo ufw allow 443/tcp
sudo ufw allow 8000:8099/tcp      # proxy-terminated WSS port range from WORKFLOW.md
sudo ufw enable
sudo apt install -y unattended-upgrades fail2ban && sudo dpkg-reconfigure -plow unattended-upgrades
timedatectl status    # systemd-timesyncd is on by default on Ubuntu; check "System clock synchronized: yes"
# /etc/ssh/sshd_config.d/10-hardening.conf: PasswordAuthentication no; PermitRootLogin no  (after key login works)
```
- Clock sync matters for TLS and ACME; VirtualBox VMs that were suspended can drift (inference).

### Inferences
- Caddy binding to 80/443 as a non-root service is handled by the official Caddy package's unit (capability); a manually copied binary may need `sudo setcap cap_net_bind_service=+ep /usr/bin/caddy`.

### Gaps
- All items in this section lack cited sources.
