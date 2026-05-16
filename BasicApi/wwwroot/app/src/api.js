/* ═══════════════════════════════════════════
   API — REST client for the backend
   ═══════════════════════════════════════════ */

const BASE = '';

/**
 * Throws on non-OK with the parsed error body (or status text).
 */
async function request(method, path, body = null, token = null) {
    const headers = { 'Content-Type': 'application/json' };
    if (token) headers['Authorization'] = `Bearer ${token}`;

    const res = await fetch(`${BASE}${path}`, {
        method,
        headers,
        body: body ? JSON.stringify(body) : null,
    });

    if (!res.ok) {
        let err;
        try { err = await res.json(); } catch { err = { title: res.statusText }; }
        throw new Error(err.title || `HTTP ${res.status}`);
    }

    // 204 No Content
    if (res.status === 204) return null;
    return res.json();
}

/* ── Auth ── */

export function login(usernameOrEmail, password) {
    return request('POST', '/api/auth/login', { usernameOrEmail, password });
}

export function register(username, email, password, displayName) {
    return request('POST', '/api/auth/register', { username, email, password, displayName });
}

/* ── Chats ── */

export function getChats(token) {
    return request('GET', '/api/chats', null, token);
}

export function getChat(chatId, token) {
    return request('GET', `/api/chats/${chatId}`, null, token);
}

export function createPrivateChat(userId, token) {
    return request('POST', `/api/chats/private/${userId}`, null, token);
}

export function getMessagesCursor(chatId, token, cursor = null, limit = 30) {
    let path = `/api/chats/${chatId}/messages/cursor?limit=${limit}`;
    if (cursor) path += `&cursor=${encodeURIComponent(cursor)}`;
    return request('GET', path, null, token);
}

export function getMessagesAt(chatId, date, token, limit = 30) {
    const iso = date instanceof Date ? date.toISOString() : date;
    return request('GET', `/api/chats/${chatId}/messages/at?date=${encodeURIComponent(iso)}&limit=${limit}`, null, token);
}

export function markRead(chatId, lastMessageId, token) {
    return request('POST', `/api/chats/${chatId}/read`, { lastMessageId }, token);
}

export function searchMessages(chatId, q, token, cursor = null, limit = 30) {
    let path = `/api/chats/${chatId}/messages/search?q=${encodeURIComponent(q)}&limit=${limit}`;
    if (cursor) path += `&cursor=${encodeURIComponent(cursor)}`;
    return request('GET', path, null, token);
}

export function searchChats(q, token, type = '', limit = 20) {
    let path = `/api/chats/search?q=${encodeURIComponent(q)}&limit=${limit}`;
    if (type) path += `&type=${encodeURIComponent(type)}`;
    return request('GET', path, null, token);
}

/* ── Users ── */

export function getUserId(usernameOrEmail, token) {
    return request('GET', `/api/users/GetUserId/${encodeURIComponent(usernameOrEmail)}`, null, token);
}

export function searchUsers(q, token, limit = 20) {
    return request('GET', `/api/users/search?q=${encodeURIComponent(q)}&limit=${limit}`, null, token);
}
