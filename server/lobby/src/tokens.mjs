import { createHmac, randomBytes, timingSafeEqual } from 'node:crypto';
import { cfg } from './config.mjs';

const b64u = (b) => Buffer.from(b).toString('base64url');
const sign = (data) => createHmac('sha256', cfg.tokenSecret).update(data).digest();

// token = base64url(JSON payload) + "." + base64url(HMAC-SHA256(secret, base64url(payload)))
export function makeJoinToken(roomId, name) {
  const payload = b64u(JSON.stringify({
    r: roomId,
    n: name,
    j: randomBytes(8).toString('hex'),
    exp: Math.floor(Date.now() / 1000) + cfg.tokenTtlSec,
  }));
  return `${payload}.${b64u(sign(payload))}`;
}

// The Unity server does the same check in ConnectionApproval; this is used by tests.
export function verifyJoinToken(token, roomId) {
  const [payload, mac] = String(token).split('.');
  if (!payload || !mac) return null;
  const good = sign(payload);
  const got = Buffer.from(mac, 'base64url');
  if (got.length !== good.length || !timingSafeEqual(got, good)) return null;
  const p = JSON.parse(Buffer.from(payload, 'base64url').toString());
  if (p.r !== roomId || p.exp < Date.now() / 1000) return null;
  return p;
}
