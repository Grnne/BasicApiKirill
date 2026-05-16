/* ═══════════════════════════════════════════
   SignalR — connection management + event handling
   ═══════════════════════════════════════════ */

import { store } from './state.js';
import { addEventLog, appendMessage } from './ui.js';
import * as api from './api.js';

const HUB_URL = '/hubs/chat';

let connection = null;
let reconnectTimer = null;

/**
 * Connect to the SignalR hub with the stored token.
 */
export function connect() {
    const token = store.get('token');
    if (!token) throw new Error('No token available');

    // If already connected, disconnect first
    if (connection) disconnect();

    connection = new signalR.HubConnectionBuilder()
        .withUrl(HUB_URL, {
            accessTokenFactory: () => token,
            transport: signalR.HttpTransportType.WebSockets,
        })
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .build();

    /* ── Register event handlers ── */

    connection.on('MessageCreated', (message) => {
        appendMessage(message);
        addEventLog('MessageCreated', message);
    });

    connection.on('ChatListUpdated', (chatId, message) => {
        // Update the last message preview in chat list
        const chats = store.get('chats').map(c => {
            if (c.chatId === chatId) {
                return { ...c, lastMessage: message, lastActivityAt: message.createdAt };
            }
            return c;
        });
        chats.sort((a, b) => new Date(b.lastActivityAt) - new Date(a.lastActivityAt));
        store.set('chats', chats);
        addEventLog('ChatListUpdated', { chatId, message });
    });

    connection.on('ChatCreated', (chatId, dto) => {
        addEventLog('ChatCreated', { chatId, ...dto });
        // Refresh chat list
        refreshChatList();
    });

    connection.on('UserOnlineChanged', (userId, isOnline) => {
        addEventLog('UserOnlineChanged', { userId, isOnline });
        // Could update an online status map
    });

    connection.on('TypingChanged', (chatId, userId, isTyping) => {
        const typing = { ...store.get('typingUsers') };
        if (isTyping) {
            if (!typing[chatId]) typing[chatId] = new Set();
            typing[chatId].add(userId);
        } else {
            if (typing[chatId]) typing[chatId].delete(userId);
        }
        store.set('typingUsers', typing);
        addEventLog('TypingChanged', { chatId, userId, isTyping });
    });

    /* ── Connection lifecycle ── */

    connection.onreconnecting((error) => {
        store.set('connected', false);
        addEventLog('SYSTEM', `Reconnecting... (${error?.message || 'unknown'})`);
    });

    connection.onreconnected((connectionId) => {
        store.set('connected', true);
        addEventLog('SYSTEM', `Reconnected (${connectionId})`);
        // Re-join current chat if any
        const currentChatId = store.get('currentChatId');
        if (currentChatId) joinChat(currentChatId);
    });

    connection.onclose((error) => {
        store.set('connected', false);
        addEventLog('SYSTEM', `Disconnected (${error?.message || 'normal'})`);
    });

    return connection.start().then(() => {
        store.set('connected', true);
        store.set('connection', connection);
        addEventLog('SYSTEM', 'Connected');
    });
}

export function disconnect() {
    if (reconnectTimer) {
        clearTimeout(reconnectTimer);
        reconnectTimer = null;
    }
    if (connection) {
        connection.stop();
        connection = null;
        store.set('connected', false);
        store.set('connection', null);
        addEventLog('SYSTEM', 'Disconnected');
    }
}

export function getConnection() {
    return connection;
}

/* ── Hub methods ── */

export function joinChat(chatId) {
    if (connection) {
        return connection.invoke('JoinChat', chatId).catch(err => {
            addEventLog('ERROR', `JoinChat failed: ${err.message}`);
        });
    }
}

export function leaveChat(chatId) {
    if (connection) {
        return connection.invoke('LeaveChat', chatId).catch(err => {
            addEventLog('ERROR', `LeaveChat failed: ${err.message}`);
        });
    }
}

export function sendMessage(chatId, text) {
    if (connection) {
        return connection.invoke('SendMessage', chatId, text).catch(err => {
            addEventLog('ERROR', `SendMessage failed: ${err.message}`);
        });
    }
}

export function sendTyping(chatId, isTyping) {
    if (connection) {
        return connection.invoke('Typing', chatId, isTyping).catch(() => {});
    }
}

/* ── Internal helpers ── */

async function refreshChatList() {
    const token = store.get('token');
    if (!token) return;
    try {
        const chats = await api.getChats(token);
        store.set('chats', chats);
    } catch (err) {
        addEventLog('ERROR', `Failed to refresh chat list: ${err.message}`);
    }
}
