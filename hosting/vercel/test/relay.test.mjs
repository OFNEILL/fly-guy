import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { readFile, mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { once } from 'node:events';
import { randomUUID } from 'node:crypto';
import { Script } from 'node:vm';
import { PGlite } from '@electric-sql/pglite';
import { Relay } from '../lib/relay.mjs';
import { createHandler } from '../api/index.mjs';

const key = 'test-owner-secret-'.repeat(4);
const schema = await readFile(new URL('../schema.sql', import.meta.url), 'utf8');
const ownerPage = await readFile(new URL('../../../FlyGuy/FlyGuy.Relay/Owner.html', import.meta.url), 'utf8');
new Script(ownerPage.match(/<script>([\s\S]*?)<\/script>/)[1]);

test('microphone text authorization, AI opt-in, deduplication, cooldown, failures and cancellation', async () => {
  const db = new PGlite();
  await db.exec(schema); await db.exec(schema); // Existing installations can run the migration again.
  const pool = poolFor(db);
  let calls = 0, seen, responder = async history => { calls++; seen = history; return 'Buzz! I found a sugar crumb for you.'; };
  const relay = new Relay(pool, key, { aiConfigured: () => true, generateReply: history => responder(history) });
  const server = createServer(createHandler(() => relay));
  server.listen(0, '127.0.0.1'); await once(server, 'listening');
  const base = `http://127.0.0.1:${server.address().port}`;
  const api = (path, token, body) => fetch(base + path, { method: body ? 'POST' : 'GET', headers: { authorization: `Bearer ${token}`, ...(body ? { 'content-type': 'application/json' } : {}) }, body: body ? JSON.stringify(body) : undefined });
  try {
    const alice = await relay.join('Alice'), bob = await relay.join('Bob');
    assert.equal((await api('/api/transcripts', key, { id: randomUUID(), text: 'Hello' })).status, 401);
    assert.equal((await api('/api/admin/ai', alice.token, { userId: alice.id, enabled: true })).status, 401);
    assert.equal((await api('/api/admin/transcripts?userId='+alice.id, bob.token)).status, 401);
    assert.equal((await api('/api/admin/ai', key, { userId: alice.id, enabled: 'yes' })).status, 400);
    assert.equal((await api('/api/transcripts', alice.token, { id: randomUUID(), text: 'x'.repeat(2001) })).status, 400);
    const first = { id: randomUUID(), text: 'Hello tiny fly' };
    assert.equal((await api('/api/transcripts', alice.token, first)).status, 200);
    assert.equal(calls, 0);
    assert.equal((await relay.transcripts(key, alice.id))[0].aiStatus, 'off');
    assert.equal((await relay.transcripts(key, bob.id)).length, 0);
    await relay.transcribe(bob.token, randomUUID(), 'Private Bob speech');
    assert.equal((await api('/api/admin/ai', key, { userId: alice.id, enabled: true })).status, 200);
    assert.equal((await relay.users(key)).find(u => u.id === alice.id).aiEnabled, true);
    const second = { id: randomUUID(), text: 'Did you find any sugar?' };
    await Promise.all([api('/api/transcripts', alice.token, second), api('/api/transcripts', alice.token, second)]);
    assert.equal(calls, 1);
    assert.equal(seen.at(-1).text, second.text);
    assert.ok(seen.every(item => item.text !== 'Private Bob speech'), 'Another user entered AI context');
    assert.equal((await relay.transcripts(key, alice.id)).length, 2);
    assert.equal((await relay.transcripts(key, alice.id))[1].aiStatus, 'sent');
    const message = await relay.poll(alice.token);
    assert.equal(message.text, 'Buzz! I found a sugar crumb for you.');
    assert.equal(await relay.poll(bob.token), null);
    await relay.acknowledge(alice.token, message.id);
    assert.equal((await api('/api/transcripts', bob.token, second)).status, 409);
    await relay.transcribe(alice.token, randomUUID(), 'Another phrase');
    assert.equal(calls, 1);
    assert.equal((await relay.transcripts(key, alice.id)).at(-1).aiStatus, 'cooldown');
    await pool.query('UPDATE fly_users SET ai_last_started = NULL');
    responder = async () => { throw new Error('secret provider error'); };
    const failed = randomUUID();
    await relay.transcribe(alice.token, failed, 'Can you hear me?');
    await relay.transcribe(alice.token, failed, 'Can you hear me?');
    assert.equal((await relay.transcripts(key, alice.id)).at(-1).aiStatus, 'failed');
    assert.equal(await relay.poll(alice.token), null);
    await pool.query('UPDATE fly_users SET ai_last_started = NULL');
    let finish, started;
    const generating = new Promise(resolve => { started = resolve; });
    responder = () => { started(); return new Promise(resolve => { finish = resolve; }); };
    const pending = relay.transcribe(alice.token, randomUUID(), 'Please reply');
    await generating;
    await relay.setAi(key, alice.id, false);
    await relay.setAi(key, alice.id, true); // A toggle back on must not revive the old request.
    finish('Buzz!'); await pending;
    assert.equal(await relay.poll(alice.token), null);
    assert.equal((await relay.transcripts(key, alice.id)).at(-1).aiStatus, 'cancelled');
    assert.equal((await api('/api/admin/transcripts?userId='+alice.id, key)).status, 200);
    await assert.rejects(new Relay(pool, key).setAi(key, bob.id, true), error => error.status === 503);
    await pool.query("UPDATE fly_transcripts SET created_at = now() - interval '25 hours'");
    assert.equal((await relay.transcripts(key, alice.id)).length, 0);
  } finally { await new Promise(resolve => server.close(resolve)); await db.close(); }
});

// PGlite runs the real Postgres engine in-process. Serialize access because it
// has one connection; production pg uses server-side row locks across instances.
function poolFor(db) {
  let tail = Promise.resolve();
  async function connect() {
    const previous = tail;
    let release;
    tail = new Promise(resolve => { release = resolve; });
    await previous;
    return { query: (sql, values) => db.query(sql, values), release };
  }
  return {
    connect,
    async query(sql, values) {
      const client = await connect();
      try { return await client.query(sql, values); } finally { client.release(); }
    }
  };
}

test('HTTP protocol, authorization, delivery and database constraints', async t => {
  const db = new PGlite();
  await db.exec(schema);
  const pool = poolFor(db);
  const relay = new Relay(pool, key);
  const server = createServer(createHandler(() => relay));
  server.listen(0, '127.0.0.1'); await once(server, 'listening');
  const base = `http://127.0.0.1:${server.address().port}`;
  async function api(path, { token, body, method, raw, contentType } = {}) {
    return fetch(base + path, {
      method: method ?? (body !== undefined || raw !== undefined ? 'POST' : 'GET'),
      headers: { ...(token ? { authorization: `Bearer ${token}` } : {}),
        ...(body !== undefined || raw !== undefined ? { 'content-type': contentType ?? 'application/json' } : {}) },
      body: raw ?? (body !== undefined ? JSON.stringify(body) : undefined)
    });
  }
  try {
    await t.test('health, malformed input and admin isolation', async () => {
      assert.equal((await api('/health')).status, 200);
      assert.equal((await api('/api/admin/users')).status, 401);
      assert.equal((await api('/api/join', { body: { displayName: '\u0001' } })).status, 400);
      assert.equal((await api('/api/join', { raw: '{broken' })).status, 400);
      assert.equal((await api('/api/join', { body: null })).status, 400);
      assert.equal((await api('/api/join', { raw: 'x'.repeat(8193) })).status, 413);
      assert.equal((await api('/api/join', { raw: '{}', contentType: 'text/plain' })).status, 415);
    });
    const alice = await (await api('/api/join', { body: { displayName: 'Alice' } })).json();
    const bob = await (await api('/api/join', { body: { displayName: 'Bob' } })).json();
    await t.test('unique identities and hashed tokens', async () => {
      assert.notEqual(alice.id, bob.id); assert.notEqual(alice.token, bob.token);
      assert.equal(alice.token.length, 64);
      assert.equal((await api('/api/admin/users', { token: alice.token })).status, 401);
      const stored = await pool.query('SELECT token_hash FROM fly_users');
      assert.ok(stored.rows.every(row => row.token_hash !== alice.token && row.token_hash !== bob.token));
    });
    let messageId;
    await t.test('send, poll, cross-user ACK isolation, repeat delivery and idempotent ACK', async () => {
      const sent = await api('/api/admin/messages', { token: key, body: { userId: alice.id, text: 'Hello from the fly!' } });
      assert.equal(sent.status, 200); ({ messageId } = await sent.json());
      assert.equal((await api('/api/inbox', { token: bob.token })).status, 204);
      assert.equal((await api(`/api/inbox/${messageId}/ack`, { token: bob.token, method: 'POST' })).status, 204);
      for (let i = 0; i < 2; i++) {
        const message = await (await api('/api/inbox', { token: alice.token })).json();
        assert.equal(message.id, messageId); assert.equal(message.text, 'Hello from the fly!');
        assert.ok(Number.isFinite(Date.parse(message.createdAt)));
      }
      for (let i = 0; i < 2; i++) assert.equal((await api(`/api/inbox/${messageId}/ack`, { token: alice.token, method: 'POST' })).status, 204);
      assert.equal((await api('/api/inbox', { token: alice.token })).status, 204);
    });
    await t.test('presence, expiry and queue counts', async () => {
      await pool.query("UPDATE fly_users SET last_seen = now() - interval '46 seconds' WHERE id = $1", [bob.id]);
      await relay.send(key, alice.id, 'Expired');
      await pool.query("UPDATE fly_messages SET created_at = now() - interval '25 hours'");
      const users = await (await api('/api/admin/users', { token: key })).json();
      assert.equal(users.find(u => u.id === alice.id).online, true);
      assert.equal(users.find(u => u.id === bob.id).online, false);
      assert.ok(users.every(u => u.pendingMessages === 0));
      assert.equal((await api('/api/inbox', { token: alice.token })).status, 204);
    });
    await t.test('queue cap holds during simultaneous sends; failures roll back cleanly', async () => {
      const results = await Promise.allSettled(Array.from({ length: 51 }, (_, i) => relay.send(key, alice.id, `Message ${i}`)));
      assert.equal(results.filter(r => r.status === 'fulfilled').length, 50);
      assert.equal(results.find(r => r.status === 'rejected').reason.status, 409);
      assert.equal((await relay.users(key)).find(u => u.id === alice.id).pendingMessages, 50);
      const message = await relay.poll(alice.token);
      assert.equal(message.text, 'Message 0');
      await relay.acknowledge(alice.token, message.id);
      await relay.send(key, alice.id, 'Room again');
      assert.equal((await api('/api/admin/messages', { token: key, body: { userId: alice.id, text: '\u0000' } })).status, 400);
      assert.equal((await api('/api/admin/messages', { token: key, body: { userId: 'invalid', text: 'Hello' } })).status, 400);
    });
    await t.test('rate limit is durable across relay instances', async () => {
      for (let i = 0; i < 240; i++) await relay.limit('rate-test');
      await assert.rejects(new Relay(pool, key).limit('rate-test'), error => error.status === 429);
    });
  } finally {
    await new Promise(resolve => server.close(resolve));
    await db.close();
  }
});

test('identities and queued messages survive closing and reopening Postgres storage', async () => {
  const directory = await mkdtemp(join(tmpdir(), 'fly-vercel-'));
  let db;
  try {
    db = new PGlite(directory); await db.exec(schema);
    let relay = new Relay(poolFor(db), key);
    const user = await relay.join('Persistent fly');
    const receipt = await relay.send(key, user.id, 'Still here');
    await db.close();
    db = new PGlite(directory);
    relay = new Relay(poolFor(db), key);
    assert.equal((await relay.poll(user.token)).id, receipt.messageId);
    assert.equal((await relay.users(key))[0].displayName, 'Persistent fly');
  } finally { if (db) await db.close(); await rm(directory, { recursive: true, force: true }); }
});

