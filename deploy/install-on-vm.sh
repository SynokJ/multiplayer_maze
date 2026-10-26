#!/usr/bin/env bash
# Run ON THE VM, once, after the certificate exists:
#   sudo bash ~/upload/deploy/install-on-vm.sh <DOMAIN>
# Installs the lobby, nginx site and systemd service from the folders you uploaded (server/ and deploy/).
set -euo pipefail
DOMAIN=${1:?usage: install-on-vm.sh <DOMAIN>   e.g. mymaze.duckdns.org}
HERE=$(cd "$(dirname "$0")" && pwd)
UP=$(dirname "$HERE")                                  # ~/upload
CERT=/etc/letsencrypt/live/$DOMAIN/fullchain.pem

[ "$(id -u)" -eq 0 ] || { echo "run with sudo"; exit 1; }
[ -f "$CERT" ] || { echo "No certificate at $CERT. Finish Step 5 first."; exit 1; }
command -v node  >/dev/null || { echo "node missing. Do Step 4 first."; exit 1; }
command -v nginx >/dev/null || { echo "nginx missing. Do Step 4 first."; exit 1; }

echo "== service account and folders"
id maze >/dev/null 2>&1 || useradd --system --home /opt/maze --shell /usr/sbin/nologin maze
mkdir -p /opt/maze/web /opt/maze/server /opt/maze/lobby /opt/maze/bin /etc/maze /var/www/acme
chown maze:maze /opt/maze

echo "== lobby code -> /opt/maze/lobby"
rm -rf /opt/maze/lobby/src /opt/maze/lobby/tools
cp -r "$UP/server/lobby/." /opt/maze/lobby/
cp "$UP/server/lobby/tools/fake-room.sh" /opt/maze/server/fake-room.sh
sed -i 's#"$(dirname "$0")/fake-room-server.mjs"#/opt/maze/lobby/tools/fake-room-server.mjs#' /opt/maze/server/fake-room.sh
chmod +x /opt/maze/server/fake-room.sh
chown -R maze:maze /opt/maze/lobby /opt/maze/server
(cd /opt/maze/lobby && sudo -u maze env HOME=/opt/maze npm install --no-audit --no-fund)

echo "== secrets and settings -> /etc/maze/lobby.env (only created once)"
if [ ! -f /etc/maze/lobby.env ]; then
  {
    echo "LOBBY_PORT=3000"
    echo "SERVER_BIN=/opt/maze/server/fake-room.sh"
    echo "SERVER_CWD=/opt/maze/server"
    echo "PORT_MIN=7000"
    echo "PORT_MAX=7099"
    echo "MAX_ROOMS=8"
    echo "PUBLIC_HOST=$DOMAIN"
    echo "PUBLIC_PORT=443"
    echo "PUBLIC_TLS=true"
    echo "TOKEN_SECRET=$(openssl rand -hex 32)"
    echo "INTERNAL_KEY=$(openssl rand -hex 16)"
  } > /etc/maze/lobby.env
  chown root:maze /etc/maze/lobby.env
  chmod 640 /etc/maze/lobby.env
fi

echo "== nginx site"
sed "s/__DOMAIN__/$DOMAIN/g" "$UP/deploy/nginx-maze.conf.template" > /etc/nginx/sites-available/maze.conf
ln -sf /etc/nginx/sites-available/maze.conf /etc/nginx/sites-enabled/maze.conf
rm -f /etc/nginx/sites-enabled/default
nginx -t
systemctl reload nginx

echo "== systemd service"
cp "$UP/deploy/maze-lobby.service" /etc/systemd/system/maze-lobby.service
install -m 755 "$UP/deploy/publish-on-vm.sh" /opt/maze/bin/publish.sh
systemctl daemon-reload
systemctl enable maze-lobby
systemctl restart maze-lobby

echo
echo "Done. Lobby on 127.0.0.1:3000, nginx on 443 for $DOMAIN."
echo "Check:  curl -s https://$DOMAIN/api/health"
