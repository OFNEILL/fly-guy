import { createHash, randomBytes, randomUUID, timingSafeEqual } from 'node:crypto';

const hash = value => createHash('sha256').update(value).digest('hex');
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export class RelayError extends Error {
  constructor(status, message) { super(message); this.status = status; }
}
const fail = (status, message) => { throw new RelayError(status, message); };
const messageRow = row => row ? { id: row.id, text: row.text, createdAt: row.created_at } : null;

export class Relay {
  constructor(pool, adminKey, { generateReply, aiConfigured = () => false } = {}) {
    if (typeof adminKey !== 'string' || adminKey.length < 32 || adminKey.length > 256)
      throw new Error('FLYGUY_ADMIN_KEY must contain 32-256 characters');
    this.pool = pool; this.adminHash = hash(adminKey);
    this.generateReply = generateReply; this.aiConfigured = aiConfigured;
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
    await client.query("DELETE FROM fly_transcripts WHERE created_at <= now() - interval '24 hours'");
  }
  async users(token) {
    this.admin(token);
    await this.prune();
    const { rows } = await this.pool.query(`
      SELECT u.id, u.display_name AS "displayName", u.last_seen AS "lastSeen",
        u.last_seen > now() - interval '45 seconds' AS online,
        count(m.id)::integer AS "pendingMessages", u.ai_enabled AS "aiEnabled"
      FROM fly_users u LEFT JOIN fly_messages m ON m.user_id = u.id
      GROUP BY u.id ORDER BY online DESC, u.display_name`);
    return rows;
  }
  async setAi(token, userId, enabled) {
    this.admin(token);
    if (!uuid.test(userId) || typeof enabled !== 'boolean') fail(400, 'Choose a user and an AI reply setting.');
    if (enabled && !this.aiConfigured()) fail(503, 'AI replies need OPENAI_MODEL and OPENAI_API_KEY on the server.');
    const { rows } = await this.pool.query('UPDATE fly_users SET ai_enabled = $2, ai_version = ai_version + 1 WHERE id = $1 RETURNING id', [userId, enabled]);
    if (!rows.length) fail(404, 'Recipient no longer exists.');
    return { aiEnabled: enabled };
  }
  async transcripts(token, userId) {
    this.admin(token);
    if (!uuid.test(userId)) fail(400, 'Invalid recipient ID.');
    await this.prune();
    const { rows } = await this.pool.query(`SELECT id, text, created_at AS "createdAt",
      CASE WHEN ai_status = 'generating' AND created_at < now() - interval '1 minute' THEN 'failed' ELSE ai_status END AS "aiStatus",
      ai_reply AS "aiReply" FROM fly_transcripts WHERE user_id = $1 ORDER BY sequence DESC LIMIT 100`, [userId]);
    return rows.reverse();
  }
  async transcribe(token, transcriptId, text) {
    const userId = await this.authenticate(token);
    const value = typeof text === 'string' ? text.trim() : '';
    if (!uuid.test(transcriptId) || !value || value.length > 2000 || /[\p{Cc}]/u.test(value)) fail(400, 'Transcripts must contain 1-2000 visible characters and a valid ID.');
    const claim = await this.transaction(async client => {
      const { rows: [user] } = await client.query('SELECT *, ai_last_started > now() - interval \'12 seconds\' AS cooling FROM fly_users WHERE id = $1 FOR UPDATE', [userId]);
      const existing = await client.query('SELECT user_id FROM fly_transcripts WHERE id = $1', [transcriptId]);
      if (existing.rows.length) {
        if (existing.rows[0].user_id !== userId) fail(409, 'Transcript ID already used.');
        return null;
      }
      await this.prune(client);
      const status = !user.ai_enabled ? 'off' : user.cooling ? 'cooldown' : 'generating';
      await client.query('INSERT INTO fly_transcripts(id, user_id, text, ai_status) VALUES ($1, $2, $3, $4)', [transcriptId, userId, value, status]);
      await client.query('UPDATE fly_users SET last_seen = now() WHERE id = $1', [userId]);
      await client.query('DELETE FROM fly_transcripts WHERE user_id = $1 AND sequence NOT IN (SELECT sequence FROM fly_transcripts WHERE user_id = $1 ORDER BY sequence DESC LIMIT 100)', [userId]);
      if (status !== 'generating') return null;
      await client.query('UPDATE fly_users SET ai_last_started = now() WHERE id = $1', [userId]);
      return { version: user.ai_version };
    });
    if (claim) {
      try {
        if (!this.aiConfigured() || !this.generateReply) throw new Error('AI not configured');
        const { rows } = await this.pool.query('SELECT text, ai_reply FROM fly_transcripts WHERE user_id = $1 ORDER BY sequence DESC LIMIT 10', [userId]);
        const reply = await this.generateReply(rows.reverse());
        if (typeof reply !== 'string' || !reply.trim() || reply.length > 500 || /[\p{Cc}]/u.test(reply)) throw new Error('Invalid AI reply');
        await this.transaction(async client => {
          const { rows: [user] } = await client.query('SELECT ai_enabled, ai_version FROM fly_users WHERE id = $1 FOR UPDATE', [userId]);
          const { rows: [inbox] } = await client.query('SELECT count(*)::integer AS count FROM fly_messages WHERE user_id = $1', [userId]);
          if (!user.ai_enabled || user.ai_version !== claim.version || inbox.count >= 50) {
            await client.query("UPDATE fly_transcripts SET ai_status = 'cancelled' WHERE id = $1", [transcriptId]);
            return;
          }
          await client.query('INSERT INTO fly_messages(id, user_id, text) VALUES ($1, $2, $3)', [randomUUID(), userId, reply.trim()]);
          await client.query("UPDATE fly_transcripts SET ai_status = 'sent', ai_reply = $2 WHERE id = $1", [transcriptId, reply.trim()]);
        });
      } catch {
        // Keep the transcript available to the owner, without leaking provider errors or retrying billable calls.
        await this.pool.query("UPDATE fly_transcripts SET ai_status = 'failed' WHERE id = $1", [transcriptId]);
      }
    }
    return { accepted: true };
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
