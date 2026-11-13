import { readFile } from 'node:fs/promises';
import { database } from '../lib/database.mjs';

const pool = database();
try {
  await pool.query(await readFile(new URL('../schema.sql', import.meta.url), 'utf8'));
  console.log('Relay database schema is ready.');
} finally { await pool.end(); }
