/* ═══════════════════════════════════════════
   Main — entry point
   ═══════════════════════════════════════════ */

import { renderApp } from './ui.js';
import { store } from './state.js';
import * as signalr from './signalr.js';
import * as api from './api.js';

// Restore token from localStorage on page load
const savedToken = localStorage.getItem('chat_token');
if (savedToken) {
    store.set('token', savedToken);
    store.set('userId', localStorage.getItem('chat_userId'));
    store.set('username', localStorage.getItem('chat_username'));
    store.set('displayName', localStorage.getItem('chat_displayName'));
}

// Persist auth state changes
store.onChange('token', (token) => {
    if (token) {
        localStorage.setItem('chat_token', token);
    } else {
        localStorage.removeItem('chat_token');
        localStorage.removeItem('chat_userId');
        localStorage.removeItem('chat_username');
        localStorage.removeItem('chat_displayName');
    }
});
store.onChange('userId', (id) => localStorage.setItem('chat_userId', id || ''));
store.onChange('username', (u) => localStorage.setItem('chat_username', u || ''));
store.onChange('displayName', (n) => localStorage.setItem('chat_displayName', n || ''));

// Render
renderApp();

// Auto-connect if token exists + load chats
if (store.get('token')) {
    signalr.connect()
        .then(() => api.getChats(store.get('token')))
        .then(chats => store.set('chats', chats))
        .catch(() => {});
}
