CREATE TABLE IF NOT EXISTS fly_relay_lock (id integer PRIMARY KEY CHECK (id = 1));
INSERT INTO fly_relay_lock VALUES (1) ON CONFLICT DO NOTHING;
CREATE TABLE IF NOT EXISTS fly_users (
    id uuid PRIMARY KEY,
    display_name text NOT NULL,
    token_hash text NOT NULL UNIQUE,
    last_seen timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS fly_messages (
    sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id uuid NOT NULL UNIQUE,
    user_id uuid NOT NULL REFERENCES fly_users(id),
    text text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS fly_messages_inbox ON fly_messages(user_id, sequence);
CREATE INDEX IF NOT EXISTS fly_messages_expiry ON fly_messages(created_at);
CREATE TABLE IF NOT EXISTS fly_rate_limits (
    key text PRIMARY KEY,
    hits integer NOT NULL,
    expires_at timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS fly_rate_expiry ON fly_rate_limits(expires_at);
