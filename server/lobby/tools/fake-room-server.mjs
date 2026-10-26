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
