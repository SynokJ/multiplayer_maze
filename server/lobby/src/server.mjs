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
