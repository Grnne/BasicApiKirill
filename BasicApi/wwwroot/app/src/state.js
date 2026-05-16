/* ═══════════════════════════════════════════
   State — simple reactive state store
   ═══════════════════════════════════════════ */

/**
 * Minimal reactive state — just a pub/sub pattern.
 * When state changes, registered listeners are called.
 */
export function createStore(initial = {}) {
    const state = { ...initial };
    const listeners = new Map(); // key → Set<fn>
    const wildcardListeners = new Set(); // any key change

    function get(key) {
        return key ? state[key] : state;
    }

    function set(key, value) {
        const changed = value !== state[key];
        if (!changed) return;
        const old = state[key];
        state[key] = value;
        // notify key-specific listeners
        const keyListeners = listeners.get(key);
        if (keyListeners) {
            for (const fn of keyListeners) fn(value, old);
        }
        // notify wildcard listeners
        for (const fn of wildcardListeners) fn(key, value, old);
    }

    function onChange(key, fn) {
        if (!listeners.has(key)) listeners.set(key, new Set());
        listeners.get(key).add(fn);
        return () => listeners.get(key).delete(fn); // unsubscribe
    }

    function onAny(fn) {
        wildcardListeners.add(fn);
        return () => wildcardListeners.delete(fn);
    }

    return { get, set, onChange, onAny, state };
}

/* ── App store ── */
export const store = createStore({
    // Auth
    token: null,
    userId: null,
    username: null,
    displayName: null,

    // SignalR
    connected: false,
    connection: null,

    // UI
    currentChatId: null,
    chats: [],
    messages: [],
    users: [],
    eventLog: [],
    typingUsers: {},   // chatId → Set<userId>
});
