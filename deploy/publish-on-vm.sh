#!/usr/bin/env bash
# Run ON THE VM after uploading builds:   sudo /opt/maze/bin/publish.sh
# Expects:  ~/upload/build-web/ (WebGL build)   and/or   ~/upload/build-server/ (Linux server build).
# Only the folders you uploaded are replaced.
set -euo pipefail
[ "$(id -u)" -eq 0 ] || { echo "run with sudo"; exit 1; }
UPLOAD=${UPLOAD_DIR:-$(getent passwd "${SUDO_USER:-root}" | cut -d: -f6)/upload}

if [ -d "$UPLOAD/build-web" ]; then
  echo "== WebGL build -> /opt/maze/web"
  rm -rf /opt/maze/web/*
  cp -r "$UPLOAD/build-web/." /opt/maze/web/
  chown -R root:www-data /opt/maze/web
  find /opt/maze/web -type d -exec chmod 755 {} +
  find /opt/maze/web -type f -exec chmod 644 {} +
fi

if [ -d "$UPLOAD/build-server" ]; then
  echo "== Linux server build -> /opt/maze/server"
  find /opt/maze/server -mindepth 1 ! -name fake-room.sh -delete
  cp -r "$UPLOAD/build-server/." /opt/maze/server/
  chmod +x /opt/maze/server/MazeServer.x86_64
  chown -R maze:maze /opt/maze/server
  # switch the lobby from the stand-in to the real Unity server
  sed -i 's#^SERVER_BIN=.*#SERVER_BIN=/opt/maze/server/MazeServer.x86_64#' /etc/maze/lobby.env
fi

nginx -t && systemctl reload nginx
systemctl restart maze-lobby        # running rooms end; fine while developing
echo "Published. Open https://$(grep '^PUBLIC_HOST=' /etc/maze/lobby.env | cut -d= -f2)/"
