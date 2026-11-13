import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import handler from '../api/index.mjs';

const page = await readFile(new URL('../../../FlyGuy/FlyGuy.Relay/Owner.html', import.meta.url));
createServer((req, res) => {
  if (req.method === 'GET' && req.url === '/') {
    res.setHeader('Content-Type', 'text/html; charset=utf-8');
    res.setHeader('Cache-Control', 'no-store'); res.end(page);
  } else handler(req, res);
}).listen(5188, '127.0.0.1', () => console.log('Relay: http://127.0.0.1:5188'));
