# Shared relay on Vercel

This is a Postgres-backed implementation of the existing Fly Guy relay API.
The Windows app and owner message desk work without protocol changes. Receiving
is enabled by default in the Windows app, with a persistent Disconnect option. The original .NET localhost relay remains available for separate testing.

## Account setup and deployment

1. Create a Vercel project from this repository. Select **Other** as the framework
   and set the root directory to `hosting/vercel`. Enable **Include source files
   outside of the Root Directory in the Build Step**: the build copies the
   existing owner desk from `FlyGuy/FlyGuy.Relay/Owner.html`.
2. Add a Neon Postgres database through Vercel Marketplace. Use its pooled
   connection string as the server-only `DATABASE_URL` environment variable.
   Retain TLS certificate verification. Never use `rejectUnauthorized: false`.
3. Generate a private owner key (Node command below) and save it in your password
   manager. Add it as server-only `FLYGUY_ADMIN_KEY` in Vercel. Do not use a
   `NEXT_PUBLIC_` prefix or bundle either secret in the desktop app.
4. Initialize the schema once against that database. From this directory, run
   `npm ci`, copy `.env.example` to `.env`, fill in the connection string and key,
   then run `npm run db:init`. The schema creation is safe to repeat. Keep
   `.env` private; it is gitignored. Schema initialization is deliberately
   separate from builds so previews cannot accidentally modify production.
5. Deploy. The production relay address is `https://YOUR-PROJECT.vercel.app`.
   Allow unauthenticated HTTP access to the production deployment in Vercel's
   Deployment Protection settings: desktop clients cannot pass an interactive
   Vercel sign-in page. API authorization still protects the recipient list,
   messages and inboxes. Keep preview deployments protected and use a separate
   preview database/key if enabling them.
6. Visit `/health`, then open `/` and unlock the message desk using the new
   owner key. The local DPAPI owner key is for the local relay only.
7. Each Windows app connects automatically to `https://fly-guy-relay.vercel.app/`
   and uses its local network IP address as the recipient name. A different
   deployment requires changing `RelayConnection.RelayAddress` and rebuilding. Local users are not migrated.
   The list refreshes within ten seconds. Test with two Windows profiles or
   computers; copies under one Windows profile share saved recipient settings.

Generate the key locally (the result is a secret, so do not paste it into chat):

```powershell
node -e "console.log(require('node:crypto').randomBytes(32).toString('hex'))"
```

## Local checks

Node 24 is used in production. From this directory:

```powershell
npm ci
npm test
node scripts/build.mjs
```

Tests run the Postgres engine through PGlite and exercise the HTTP API, token
isolation, delivery/acknowledgment, persistence, expiry, queue limits and shared
rate limiting. PGlite serializes one connection; live multi-instance row locking
and Vercel routing still need a deployed smoke check. For local manual testing
against a real Postgres database, set `.env`, initialize it and run `npm start`.
The local Node relay listens at `http://127.0.0.1:5188`.

## Operations

User names, hashed tokens, presence and plaintext queued messages are stored in
Postgres. Messages expire after 24 hours and each inbox holds 50 messages.
Presence expires after 45 seconds. Database row locks protect registration and
queue caps across function instances. IP/minute hashes with a two-minute expiry
provide a shared 240-request/minute rate limit; source IPs are not stored here.
Expired rows are removed during traffic, rather than by a background process.
The hosting provider may separately retain request logs.

Do not use the same database for unrelated deployments. Enable database backups
through the provider. Rotate the admin key in Vercel and redeploy to apply it;
recipient tokens remain valid. Regular five-second client polling and database
queries consume hosting usage even when no messages are sent; review your
account's usage limits before sharing widely.

References: [Vercel Node functions](https://vercel.com/docs/functions/runtimes/node-js),
[Vercel root directory](https://vercel.com/docs/deployments/configure-a-build#root-directory),
[node-postgres pools](https://node-postgres.com/apis/pool).
