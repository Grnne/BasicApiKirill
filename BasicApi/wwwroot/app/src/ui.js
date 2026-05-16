/* ═══════════════════════════════════════════
   UI — DOM helpers and rendering
   ═══════════════════════════════════════════ */

import { store } from './state.js';
import * as api from './api.js';
import * as signalr from './signalr.js';

let typingTimer = null;
const TYPING_THROTTLE = 2000;

/* ── DOM helpers ── */

export function $(selector, parent = document) {
    return parent.querySelector(selector);
}

export function $$(selector, parent = document) {
    return [...parent.querySelectorAll(selector)];
}

const EVENT_MAP = {
    onclick: 'click',
    onkeydown: 'keydown',
    onkeyup: 'keyup',
    onblur: 'blur',
    oninput: 'input',
    onchange: 'change',
    onmouseenter: 'mouseenter',
    onmouseleave: 'mouseleave',
    onfocus: 'focus',
};

export function el(tag, attrs = {}, children = []) {
    const elem = document.createElement(tag);
    for (const [key, val] of Object.entries(attrs)) {
        if (key === 'className') elem.className = val;
        else if (key === 'style' && typeof val === 'object') Object.assign(elem.style, val);
        else if (key.startsWith('on')) {
            const eventName = EVENT_MAP[key] || key.slice(2).toLowerCase();
            elem.addEventListener(eventName, val);
        } else if (key === 'dataset') Object.assign(elem.dataset, val);
        else elem.setAttribute(key, val);
    }
    for (const child of children) {
        if (typeof child === 'string') elem.appendChild(document.createTextNode(child));
        else if (child instanceof Node) elem.appendChild(child);
    }
    return elem;
}

/* ── Event log ── */

export function addEventLog(type, data) {
    const container = document.getElementById('event-log');
    if (!container) return;

    const time = new Date().toLocaleTimeString();
    const text = typeof data === 'object' && data !== null
        ? JSON.stringify(data, null, 1).replace(/\n/g, ' ')
        : String(data);

    const row = el('div', { className: 'ev-row' }, [
        el('span', { className: 'ev-time' }, `[${time}]`),
        ' ',
        el('span', { className: 'ev-type' }, type),
        ': ',
        el('span', { className: 'ev-data' }, text),
    ]);

    row.style.color = type === 'ERROR' ? 'var(--danger)' : type === 'SYSTEM' ? 'var(--text-dim)' : 'var(--accent)';

    container.appendChild(row);
    container.scrollTop = container.scrollHeight;
}

/* ── Utilities ── */

function truncate(text, max) {
    if (!text) return '';
    return text.length > max ? text.slice(0, max) + '...' : text;
}

/* ═══════════════════════════════════════════
   APP RENDER
   ═══════════════════════════════════════════ */

export function renderApp() {
    renderLeftPanel();
    updateStatusBar();
    setupSubscriptions();
}

/* ═══════════════════════════════════════════
   STATUS BAR
   ═══════════════════════════════════════════ */

function updateStatusBar() {
    const indicator = document.getElementById('status-indicator');
    const text = document.getElementById('status-text');
    const userDisplay = document.getElementById('user-display');
    const connected = store.get('connected');
    const token = store.get('token');

    if (indicator) indicator.className = connected ? 'connected' : '';
    if (text) text.textContent = connected ? 'Connected' : 'Disconnected';
    if (userDisplay) userDisplay.textContent = token
        ? `@${store.get('displayName') || store.get('username') || '???'}`
        : 'Not logged in';
}

/* ═══════════════════════════════════════════
   LEFT PANEL
   ═══════════════════════════════════════════ */

function renderLeftPanel() {
    const panel = document.getElementById('left-panel');
    if (!panel) return;
    panel.innerHTML = '';

    if (!store.get('token')) {
        panel.appendChild(createAuthForm());
        return;
    }

    panel.appendChild(createChatList());

    panel.appendChild(el('div', { className: 'section' }, [
        el('div', { className: 'section-header' }, [
            el('h3', {}, 'Events'),
            el('button', { onclick: () => { const log = document.getElementById('event-log'); if (log) log.innerHTML = ''; } }, 'Clear'),
        ]),
        el('div', { id: 'event-log' }),
    ]));
}

/* ── Auth ── */

function createAuthForm() {
    let isRegisterMode = false;

    const usernameInput = el('input', { id: 'auth-username', placeholder: 'Username / Email', className: 'grow' });
    const passwordInput = el('input', { id: 'auth-password', type: 'password', placeholder: 'Password', className: 'grow', onkeydown: (e) => { if (e.key === 'Enter') submit(); } });
    const emailInput = el('input', { id: 'auth-email', type: 'email', placeholder: 'Email (for register)', className: 'grow', style: { display: 'none' } });
    const displayNameInput = el('input', { id: 'auth-displayname', placeholder: 'Display Name (for register)', className: 'grow', style: { display: 'none' } });
    const submitBtn = el('button', { id: 'auth-submit' }, 'Login');
    const toggleBtn = el('button', { id: 'auth-toggle', onclick: toggleMode }, 'Switch to Register');

    function toggleMode() {
        isRegisterMode = !isRegisterMode;
        const showReg = isRegisterMode ? '' : 'none';
        emailInput.style.display = showReg;
        displayNameInput.style.display = showReg;
        submitBtn.textContent = isRegisterMode ? 'Register' : 'Login';
        toggleBtn.textContent = isRegisterMode ? 'Switch to Login' : 'Switch to Register';
    }

    async function submit() {
        const usernameOrEmail = usernameInput.value.trim();
        const password = passwordInput.value;
        if (!usernameOrEmail || !password) return;

        try {
            const res = isRegisterMode
                ? await api.register(usernameOrEmail, emailInput.value.trim(), password, displayNameInput.value.trim() || null)
                : await api.login(usernameOrEmail, password);

            store.set('token', res.token);
            store.set('userId', res.userId);
            store.set('username', res.username);
            store.set('displayName', res.displayName);

            await signalr.connect();
            const chats = await api.getChats(res.token);
            store.set('chats', chats);
            renderLeftPanel();
            updateStatusBar();
        } catch (err) {
            addEventLog('ERROR', `${isRegisterMode ? 'Register' : 'Login'} failed: ${err.message}`);
        }
    }

    return el('div', { className: 'section' }, [
        el('h3', {}, 'Login / Register'),
        el('div', { className: 'col' }, [
            usernameInput,
            passwordInput,
            emailInput,
            displayNameInput,
            el('div', { className: 'row' }, [submitBtn, toggleBtn]),
        ]),
    ]);
}

/* ── Chat list ── */

function createChatList() {
    const section = el('div', { className: 'section' }, [
        el('div', { className: 'section-header' }, [
            el('h3', {}, 'Chats'),
            el('div', { className: 'row', style: { gap: '4px' } }, [
                el('button', { onclick: () => window.open('/swagger', '_blank') }, 'Swagger'),
                el('button', { onclick: chatActions.refresh }, 'Refresh'),
            ]),
        ]),
        el('div', { className: 'row', style: { marginBottom: '8px' } }, [
            el('input', { id: 'chat-search', placeholder: 'Search chats...', className: 'grow', onkeyup: (e) => { if (e.key === 'Enter') chatActions.searchChats(); } }),
            el('button', { onclick: chatActions.searchChats }, 'Go'),
        ]),
        el('div', { className: 'row', style: { marginBottom: '8px' } }, [
            el('input', { id: 'user-search', placeholder: 'Find users...', className: 'grow', onkeyup: (e) => { if (e.key === 'Enter') chatActions.searchUsers(); } }),
            el('button', { onclick: chatActions.searchUsers }, 'Find'),
        ]),
        el('button', {
            onclick: chatActions.disconnectAndLogout,
            style: { marginBottom: '8px', width: '100%', color: 'var(--danger)', borderColor: 'var(--danger)' },
        }, 'Disconnect & Logout'),
        el('div', { id: 'chat-list' }),
    ]);

    renderChatListItems();
    return section;
}

const chatActions = {
    async refresh() {
        const token = store.get('token');
        if (!token) return;
        try {
            store.set('chats', await api.getChats(token));
        } catch (err) {
            addEventLog('ERROR', `Failed to load chats: ${err.message}`);
        }
    },

    async searchChats() {
        const q = document.getElementById('chat-search').value.trim();
        if (!q) { chatActions.refresh(); return; }
        const token = store.get('token');
        try {
            const res = await api.searchChats(q, token);
            store.set('chats', res.items);
        } catch (err) {
            addEventLog('ERROR', `Chat search failed: ${err.message}`);
        }
    },

    async searchUsers() {
        const q = document.getElementById('user-search').value.trim();
        if (!q) return;
        const token = store.get('token');
        try {
            const res = await api.searchUsers(q, token);
            store.set('users', res.items);
            renderUserResults();
        } catch (err) {
            addEventLog('ERROR', `User search failed: ${err.message}`);
        }
    },

    disconnectAndLogout() {
        signalr.disconnect();
        store.set('token', null);
        store.set('userId', null);
        store.set('username', null);
        store.set('displayName', null);
        store.set('chats', []);
        store.set('currentChatId', null);
        store.set('messages', []);
        store.set('users', []);
        renderLeftPanel();
        renderCenterPanel();
        updateStatusBar();
    },
};

function renderChatListItems() {
    const container = document.getElementById('chat-list');
    if (!container) return;
    const chats = store.get('chats');
    container.innerHTML = '';
    if (chats.length === 0) {
        container.appendChild(el('div', { style: { color: 'var(--text-dim)', padding: '8px' } }, 'No chats'));
        return;
    }
    for (const chat of chats) {
        const isActive = chat.chatId === store.get('currentChatId');
        const title = chat.type === 'group' ? (chat.title || 'Group') : (chat.companionName || 'Unknown');
        const item = el('div', {
            className: `chat-item${isActive ? ' active' : ''}`,
            dataset: { chatId: chat.chatId },
            onclick: () => selectChat(chat.chatId),
        }, [
            el('div', { className: 'chat-item-title' }, [
                title,
                chat.unreadCount > 0 ? el('span', { className: 'unread-badge' }, String(chat.unreadCount)) : null,
            ].filter(Boolean)),
            el('div', { className: 'chat-item-preview' }, chat.lastMessage
                ? `${chat.lastMessage.senderName || '?'}: ${truncate(chat.lastMessage.text, 80)}`
                : 'No messages'),
        ]);
        container.appendChild(item);
    }
}

/* ── User results ── */

function renderUserResults() {
    const container = document.getElementById('chat-list');
    if (!container) return;

    for (const el of container.querySelectorAll('.user-result-header, .user-result-item')) {
        el.remove();
    }

    const users = store.get('users');
    if (users.length === 0) return;

    container.appendChild(el('div', { className: 'user-result-header' }, ['Users:']));
    for (const user of users) {
        const item = el('div', { className: 'user-result-item', onclick: () => startPrivateChat(user.userId) }, [
            el('span', {}, user.displayName || user.username),
            el('span', { style: { color: 'var(--text-dim)', fontSize: '11px', marginLeft: '8px' } }, `@${user.username}`),
        ]);
        container.appendChild(item);
    }
}

/* ── Chat selection ── */

async function selectChat(chatId) {
    const prevChatId = store.get('currentChatId');
    if (prevChatId && prevChatId !== chatId) {
        signalr.leaveChat(prevChatId);
    }

    store.set('currentChatId', chatId);

    const token = store.get('token');
    if (!signalr.getConnection() || !store.get('connected')) {
        addEventLog('WARN', 'Not connected to SignalR, cannot join chat');
    } else {
        signalr.joinChat(chatId);
    }

    let loadedMessages = [];
    try {
        const res = await api.getMessagesCursor(chatId, token);
        loadedMessages = res.items;
    } catch (err) {
        addEventLog('ERROR', `Failed to load messages: ${err.message}`);
    }

    renderCenterPanel(loadedMessages);

    if (loadedMessages.length > 0) {
        api.markRead(chatId, loadedMessages[loadedMessages.length - 1].id, token).catch(() => {});
    }

    const chats = store.get('chats').map(c =>
        c.chatId === chatId ? { ...c, unreadCount: 0 } : c
    );
    store.set('chats', chats);
    renderChatListItems();
}

async function startPrivateChat(userId) {
    const token = store.get('token');
    try {
        const res = await api.createPrivateChat(userId, token);
        addEventLog('SYSTEM', `Private chat created/opened: ${res.chatId}`);
        store.set('chats', await api.getChats(token));
        renderChatListItems();
        selectChat(res.chatId);
    } catch (err) {
        addEventLog('ERROR', `Failed to create private chat: ${err.message}`);
    }
}

/* ═══════════════════════════════════════════
   CENTER PANEL — Messages
   ═══════════════════════════════════════════ */

function renderCenterPanel(messages) {
    const chatId = store.get('currentChatId');

    const header = document.getElementById('chat-header');
    const msgList = document.getElementById('message-list');
    const inputArea = document.getElementById('message-input-area');

    header.innerHTML = '';
    msgList.innerHTML = '';
    inputArea.innerHTML = '';

    if (!chatId) {
        msgList.appendChild(el('div', { id: 'empty-state', style: { padding: '24px', textAlign: 'center', color: 'var(--text-dim)' } }, 'Select a chat'));
        return;
    }

    renderChatHeader(chatId, header);
    renderMessageList(msgList, messages || store.get('messages'));
    renderMessageInput(chatId, inputArea);
}

/* ── Chat header ── */

async function renderChatHeader(chatId, header) {
    let title = 'Chat';

    try {
        const detail = await api.getChat(chatId, store.get('token'));
        if (store.get('currentChatId') !== chatId) return;
        title = detail.type === 'group'
            ? (detail.title || 'Group Chat')
            : detail.participants.map(p => p.displayName).join(', ');
    } catch {
        if (store.get('currentChatId') !== chatId) return;
        title = 'Chat';
    }

    header.append(
        el('h3', { style: { margin: 0 } }, title),
        el('button', {
            onclick: () => {
                signalr.leaveChat(chatId);
                store.set('currentChatId', null);
                renderCenterPanel();
                renderChatListItems();
            },
        }, 'Close'),
    );
}

/* ── Message list ── */

function renderMessageList(list, messages) {
    const userId = store.get('userId');
    for (const msg of messages) {
        list.appendChild(createMessageElement(msg, userId));
    }
    list.scrollTop = list.scrollHeight;
}

/* ── Message element factory ── */

function createMessageElement(msg, userId) {
    const isMine = msg.senderId === userId;
    return el('div', {
        className: `message${isMine ? ' mine' : ''}`,
        dataset: { msgId: msg.id },
    }, [
        el('div', { className: 'message-sender' }, isMine ? 'You' : msg.senderName),
        el('div', { className: 'message-text' }, msg.text),
        el('div', { className: 'message-time' }, new Date(msg.createdAt).toLocaleTimeString()),
    ]);
}

/* ── Append a single message (incremental update from SignalR) ── */

export function appendMessage(msg) {
    const list = document.getElementById('message-list');
    if (!list) return;
    const userId = store.get('userId');

    // Skip duplicates
    if (list.querySelector(`[data-msg-id="${msg.id}"]`)) return;

    list.appendChild(createMessageElement(msg, userId));

    // Auto-scroll if near bottom
    const isNearBottom = list.scrollHeight - list.scrollTop - list.clientHeight < 100;
    if (isNearBottom) scrollMessageListToBottom(list);
}

function scrollMessageListToBottom(list) {
    list.scrollTop = list.scrollHeight;
}

/* ── Message input ── */

function renderMessageInput(chatId, area) {
    area.appendChild(el('div', { className: 'row' }, [
        el('input', {
            id: 'msg-input',
            placeholder: 'Type a message...',
            className: 'grow',
            onkeydown: (e) => {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    doSend(chatId);
                }
                throttleTyping(chatId, true);
            },
            onblur: () => signalr.sendTyping(chatId, false),
        }),
        el('button', { onclick: () => doSend(chatId) }, 'Send'),
    ]));
}

function throttleTyping(chatId, isTyping) {
    if (typingTimer) return;
    signalr.sendTyping(chatId, true);
    typingTimer = setTimeout(() => { typingTimer = null; }, TYPING_THROTTLE);
}

async function doSend(chatId) {
    const input = document.getElementById('msg-input');
    if (!input) return;
    const text = input.value.trim();
    if (!text) return;
    input.value = '';
    if (typingTimer) {
        clearTimeout(typingTimer);
        typingTimer = null;
    }
    signalr.sendTyping(chatId, false);
    try {
        await signalr.sendMessage(chatId, text);
    } catch (err) {
        addEventLog('ERROR', `Send failed: ${err.message}`);
    }
}

/* ═══════════════════════════════════════════
   SUBSCRIPTIONS
   ═══════════════════════════════════════════ */

function setupSubscriptions() {
    store.onChange('connected', () => updateStatusBar());
    store.onChange('chats', () => renderChatListItems());
    // messages update is done via selectChat/renderCenterPanel or appendMessage
}