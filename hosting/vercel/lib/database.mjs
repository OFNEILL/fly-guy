import pg from 'pg';

let pool;
export function database() {
  if (!process.env.DATABASE_URL) throw new Error('DATABASE_URL is required');
  if (!pool) {
    pool = new pg.Pool({
      connectionString: process.env.DATABASE_URL,
      max: 3, connectionTimeoutMillis: 5000, idleTimeoutMillis: 10000,
      statement_timeout: 5000, allowExitOnIdle: true
    });
    pool.on('error', error => console.error('Relay idle database connection failed:', error.code ?? error.name));
  }
  return pool;
}
