import { createHash, randomBytes, randomUUID, timingSafeEqual } from 'node:crypto';

const hash = value => createHash('sha256').update(value).digest('hex');
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export class RelayError extends Error {
  constructor(status, message) { super(message); this.status = status; }
}
const fail = (status, message) => { throw new RelayError(status, message); };
const messageRow = row => row ? { id: row.id, text: row.text, createdAt: row.created_at } : null;

export class Relay {
  constructor(pool, adminKey) {
    if (typeof adminKey !== 'string' || adminKey.length < 32 || adminKey.length > 256)
      throw new Error('FLYGUY_ADMIN_KEY must contain 32-256 characters');
    this.pool = pool; this.adminHash = hash(adminKey);
  }
  admin(token) {
    if (typeof token !== 'string' || token.length > 256 ||
        !timingSafeEqual(Buffer.from(hash(token)), Buffer.from(this.adminHash)))
      fail(401, 'Access denied. Check your owner key.');
  }
  async transaction(action) {
    const client = await this.pool.connect();
    try {
      await client.query('BEGIN');
      const result = await action(client);
      await client.query('COMMIT');
      return result;
    } catch (error) { await client.query('ROLLBACK'); throw error; }
    finally { client.release(); }
  }
  async limit(ip) {
    // Shared across function instances. Store only a hash of the address/minute.
    const minute = Math.floor(Date.now() / 60000);
    const key = hash(`${ip}:${minute}`);
    const { rows } = await this.pool.query(`
      INSERT INTO fly_rate_limits(key, hits, expires_at) VALUES ($1, 1, now() + interval '2 minutes')
      ON CONFLICT (key) DO UPDATE SET hits = LEAST(fly_rate_limits.hits + 1, 241) RETURNING hits`, [key]);
    await this.pool.query('DELETE FROM fly_rate_limits WHERE expires_at < now()');
    if (rows[0].hits > 240) fail(429, 'Too many requests. Wait a moment and try again.');
  }
  async join(displayName) {
    const name = typeof displayName === 'string' ? displayName.trim() : '';
    if (!name || name.length > 40 || /[\p{Cc}]/u.test(name)) fail(400, 'Choose a name of 1-40 visible characters.');
    return this.transaction(async client => {
      await client.query('SELECT id FROM fly_relay_lock WHERE id = 1 FOR UPDATE');
      const { rows } = await client.query('SELECT count(*)::integer AS count FROM fly_users');
      if (rows[0].count >= 5000) fail(409, 'Relay user limit reached.');
      const id = randomUUID(), token = randomBytes(32).toString('hex').toUpperCase();
      await client.query('INSERT INTO fly_users(id, display_name, token_hash) VALUES ($1, $2, $3)', [id, name, hash(token)]);
      return { id, token, displayName: name };
    });
  }
  async authenticate(token) {
    if (typeof token !== 'string' || token.length !== 64) fail(401, 'Recipient connection was rejected. Reconnect to register again.');
    const { rows } = await this.pool.query('SELECT id FROM fly_users WHERE token_hash = $1', [hash(token)]);
    if (!rows.length) fail(401, 'Recipient connection was rejected. Reconnect to register again.');
    return rows[0].id;
  }
  async prune(client = this.pool) {
    await client.query("DELETE FROM fly_messages WHERE created_at <= now() - interval '24 hours'");
  }
  async users(token) {
    this.admin(token);
    await this.prune();
    const { rows } = await this.pool.query(`
      SELECT u.id, u.display_name AS "displayName", u.last_seen AS "lastSeen",
        u.last_seen > now() - interval '45 seconds' AS online,
        count(m.id)::integer AS "pendingMessages"
      FROM fly_users u LEFT JOIN fly_messages m ON m.user_id = u.id
      GROUP BY u.id ORDER BY online DESC, u.display_name`);
    return rows;
  }
  async poll(token) {
    const id = await this.authenticate(token);
    await this.pool.query('UPDATE fly_users SET last_seen = now() WHERE id = $1', [id]);
    await this.prune();
    const { rows } = await this.pool.query('SELECT id, text, created_at FROM fly_messages WHERE user_id = $1 ORDER BY sequence LIMIT 1', [id]);
    return messageRow(rows[0]);
  }
  async acknowledge(token, messageId) {
    const id = await this.authenticate(token);
    if (!uuid.test(messageId)) fail(400, 'Invalid message ID.');
    await this.pool.query('DELETE FROM fly_messages WHERE user_id = $1 AND id = $2', [id, messageId]);
  }
  async send(token, userId, text) {
    this.admin(token);
    const value = typeof text === 'string' ? text.trim() : '';
    if (!value || value.length > 500 || /[\x00-\x08\x0b\x0c\x0e-\x1f\x7f-\x9f]/u.test(value))
      fail(400, 'Messages must contain 1-500 characters.');
    if (typeof userId !== 'string' || !uuid.test(userId)) fail(400, 'Invalid recipient ID.');
    return this.transaction(async client => {
      const user = await client.query('SELECT id FROM fly_users WHERE id = $1 FOR UPDATE', [userId]);
      if (!user.rows.length) fail(404, 'Recipient no longer exists.');
      await this.prune(client);
      const { rows } = await client.query('SELECT count(*)::integer AS count FROM fly_messages WHERE user_id = $1', [userId]);
      if (rows[0].count >= 50) fail(409, 'Recipient inbox is full.');
      const messageId = randomUUID();
      await client.query('INSERT INTO fly_messages(id, user_id, text) VALUES ($1, $2, $3)', [messageId, userId, value]);
      return { messageId };
    });
  }
}
