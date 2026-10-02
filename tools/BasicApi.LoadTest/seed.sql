-- Synthetic data for the load run (docs/load-testing.md).
--
-- psql variables: users — number of users; peers — how many neighbours each user has an
-- "outgoing" private chat with (2 × peers chats per user in total); messages — messages
-- per chat. peers must be less than users / 2, otherwise pairs repeat.
--
-- Ids are deterministic (md5 of the number): the load program computes them the same way
-- and never reads the database. Every user has a live sign-in (session), so access tokens
-- the program issues with that sid are accepted by the hub and REST, which both check it.
--
-- These users have no working password: they cannot sign in via /api/auth/login.

\set ON_ERROR_STOP on

BEGIN;

INSERT INTO users (id, username, email, password_hash, display_name, created_at, is_active)
SELECT md5('load-user-' || i)::uuid, 'load_' || i, 'load_' || i || '@load.test',
       '!load-test-no-login', 'Load User ' || i, now() - interval '60 days', true
FROM generate_series(1, :users) AS i;

INSERT INTO sessions (id, user_id, family_id, refresh_token_hash, created_at, expires_at)
SELECT md5('load-session-' || i)::uuid, md5('load-user-' || i)::uuid, md5('load-family-' || i)::uuid,
       md5('load-refresh-' || i) || md5('load-refresh-2-' || i), now(), now() + interval '30 days'
FROM generate_series(1, :users) AS i;

-- Pair (i, i + k): the chat of user i with its k-th neighbour around the circle.
CREATE TEMP TABLE load_pairs ON COMMIT DROP AS
SELECT md5('load-chat-' || i || '-' || k)::uuid AS chat_id,
       md5('load-user-' || i)::uuid AS a,
       md5('load-user-' || ((i - 1 + k) % :users + 1))::uuid AS b
FROM generate_series(1, :users) AS i, generate_series(1, :peers) AS k;

INSERT INTO chats (id, type, created_at, private_key, last_seq)
SELECT chat_id, 'private', now() - interval '30 days',
       LEAST(a, b)::text || ':' || GREATEST(a, b)::text, :messages
FROM load_pairs;

-- The second member has the last two messages unread, so the chat list counts unread messages.
INSERT INTO chat_members (chat_id, user_id, joined_at, last_read_seq)
SELECT chat_id, a, now() - interval '30 days', :messages FROM load_pairs
UNION ALL
SELECT chat_id, b, now() - interval '30 days', GREATEST(:messages - 2, 0) FROM load_pairs;

INSERT INTO messages (id, chat_id, sender_id, text, created_at, seq)
SELECT md5(p.chat_id::text || '-' || s)::uuid, p.chat_id,
       CASE WHEN s % 2 = 0 THEN p.a ELSE p.b END,
       'Сообщение ' || s || ': обсуждаем запуск и проверяем поиск по истории',
       now() - interval '30 days' + s * interval '1 minute', s
FROM load_pairs AS p, generate_series(1, :messages) AS s;

COMMIT;

ANALYZE users, sessions, chats, chat_members, messages;

SELECT (SELECT COUNT(*) FROM users) AS users,
       (SELECT COUNT(*) FROM chats) AS chats,
       (SELECT COUNT(*) FROM chat_members) AS chat_members,
       (SELECT COUNT(*) FROM messages) AS messages,
       pg_size_pretty(pg_database_size(current_database())) AS db_size;
