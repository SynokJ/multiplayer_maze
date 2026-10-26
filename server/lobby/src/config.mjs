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
