import { database } from '../lib/database.mjs';
import { Relay, RelayError } from '../lib/relay.mjs';

export function createHandler(getRelay) {
  return async function handler(req, res) {
    res.setHeader('Cache-Control', 'no-store');
    res.setHeader('X-Content-Type-Options', 'nosniff');
    const reply = (status, body) => {
      res.statusCode = status;
      if (body === undefined) return res.end();
      res.setHeader('Content-Type', 'application/json');
      res.end(JSON.stringify(body));
    };
    try {
      const path = new URL(req.url, 'http://localhost').pathname;
      if (req.method === 'GET' && path === '/health') return reply(200, { service: 'Fly Guy relay' });
      const relay = getRelay();
      const authorization = req.headers.authorization ?? '';
      const token = authorization.startsWith('Bearer ') ? authorization.slice(7) : undefined;
      // Vercel overwrites x-vercel-forwarded-for with the client IP. Local mode uses the socket.
      const ip = process.env.VERCEL ? req.headers['x-vercel-forwarded-for'] ?? 'unknown' : req.socket?.remoteAddress ?? 'local';
      await relay.limit(ip);
      let body = {};
      if (req.method === 'POST' && !path.endsWith('/ack')) {
        if (!/^application\/json(?:\s*;|$)/i.test(req.headers['content-type'] ?? ''))
          throw new RelayError(415, 'Use application/json.');
        if (Number(req.headers['content-length'] ?? 0) > 8192) throw new RelayError(413, 'Request body is too large.');
        if (req.body !== undefined) {
          const raw = typeof req.body === 'string' ? req.body : JSON.stringify(req.body);
          if (Buffer.byteLength(raw) > 8192) throw new RelayError(413, 'Request body is too large.');
          body = JSON.parse(raw);
        } else {
          const chunks = []; let size = 0;
          for await (const chunk of req) {
            size += Buffer.byteLength(chunk);
            if (size > 8192) throw new RelayError(413, 'Request body is too large.');
            chunks.push(Buffer.from(chunk));
          }
          body = JSON.parse(Buffer.concat(chunks).toString('utf8'));
        }
        if (!body || Array.isArray(body) || typeof body !== 'object') throw new RelayError(400, 'Invalid request body.');
      }
      if (req.method === 'POST' && path === '/api/join') return reply(200, await relay.join(body.displayName));
      if (req.method === 'GET' && path === '/api/admin/users') return reply(200, await relay.users(token));
      if (req.method === 'POST' && path === '/api/admin/messages') return reply(200, await relay.send(token, body.userId, body.text));
      if (req.method === 'GET' && path === '/api/inbox') {
        const message = await relay.poll(token);
        return message ? reply(200, message) : reply(204);
      }
      const ack = /^\/api\/inbox\/([^/]+)\/ack$/.exec(path);
      if (req.method === 'POST' && ack) { await relay.acknowledge(token, ack[1]); return reply(204); }
      return reply(404, { error: 'Route not found.' });
    } catch (error) {
      if (error instanceof RelayError) return reply(error.status, { error: error.message });
      if (error instanceof SyntaxError) return reply(400, { error: 'Invalid JSON.' });
      // Never return database errors, connection strings or credentials to clients.
      console.error('Relay request failed:', error.code ?? error.name);
      return reply(503, { error: 'Relay unavailable. Try again shortly.' });
    }
  };
}

let relay;
export default createHandler(() => relay ??= new Relay(database(), process.env.FLYGUY_ADMIN_KEY));
