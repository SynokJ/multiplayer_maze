// tools/gen-port-range.mjs   usage: node gen-port-range.mjs 8000 8099 7000 > /etc/nginx/conf.d/maze-ports.conf
const [pubStart, pubEnd, privStart] = process.argv.slice(2).map(Number);
let out = '# generated; one TLS listener per public port\n';
for (let p = pubStart; p <= pubEnd; p++) {
  const t = privStart + (p - pubStart);
  out += `server {
  listen ${p} ssl;
  server_name _;
  ssl_certificate     /etc/letsencrypt/live/maze.duckdns.org/fullchain.pem;
  ssl_certificate_key /etc/letsencrypt/live/maze.duckdns.org/privkey.pem;
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
