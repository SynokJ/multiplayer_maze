// tools/ws-router.mjs
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
