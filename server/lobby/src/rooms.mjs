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
