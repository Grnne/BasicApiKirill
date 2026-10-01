<script setup lang="ts">
import { defineAsyncComponent, ref } from 'vue'
import { useRouter } from 'vue-router'

import BaseButton from '@/shared/ui/BaseButton.vue'
import ConnectionStatus from '@/features/realtime/ui/ConnectionStatus.vue'
import ChatListPanel from '@/features/chat-list/ui/ChatListPanel.vue'
import ChatWindow from '@/features/messages/ui/ChatWindow.vue'
import UserSearchPanel from '@/features/user-search/ui/UserSearchPanel.vue'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { useChatListStore } from '@/features/chat-list/model/chat-list.store'
import { useMessagesStore } from '@/features/messages/model/messages.store'
import { useAccountStore } from '@/entities/user/model/account.store'
import { useSyncStore } from '@/features/realtime/model/sync.store'

const auth = useAuthStore()
const chatList = useChatListStore()
const messages = useMessagesStore()
const account = useAccountStore()
const sync = useSyncStore()
const router = useRouter()

// Dynamic and dev-only: a static import would put the panel into the prod bundle.
const EventLog = import.meta.env.DEV
  ? defineAsyncComponent(() => import('@/features/realtime/ui/EventLog.vue'))
  : null

const isDev = import.meta.env.DEV

// One search field feeds both the chat and the user search panels.
const query = ref('')

async function onUserSelected(userId: string): Promise<void> {
  await chatList.openPrivateChat(userId)
  query.value = ''
}

async function onLogout(): Promise<void> {
  // No user data may survive logout: stores are cleared before the tokens.
  messages.reset()
  chatList.reset()
  sync.stop()
  await auth.logout()
  await router.replace({ name: 'login' })
}
</script>

<template>
  <div class="page">
    <header class="bar">
      <span class="brand">Basic<span class="accent">Chat</span></span>
      <ConnectionStatus />
      <span class="user">{{ account.me?.displayName ?? auth.user?.displayName }}</span>
      <BaseButton variant="ghost" @click="onLogout">Выйти</BaseButton>
    </header>

    <div :class="['body', { 'with-log': isDev, 'chat-open': chatList.selectedChatId !== null }]">
      <aside class="sidebar">
        <div class="search">
          <input
            v-model="query"
            class="search-input"
            type="search"
            placeholder="Поиск чатов и людей"
            autocomplete="off"
          />
        </div>
        <div class="panels">
          <ChatListPanel :query="query" />
          <UserSearchPanel :query="query" @select="onUserSelected" />
        </div>
      </aside>

      <main class="main">
        <ChatWindow />
      </main>

      <component :is="EventLog" v-if="EventLog" />
    </div>
  </div>
</template>

<style scoped>
.page {
  display: grid;
  grid-template-rows: auto 1fr;
  height: 100%;
}
.bar {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 10px 16px;
  border-bottom: 1px solid var(--border);
  background: var(--surface-solid);
}
.brand {
  font-weight: 700;
}
.accent {
  color: var(--accent);
}
.user {
  margin-left: auto;
  color: var(--text-dim);
}
.body {
  display: grid;
  grid-template-columns: 300px minmax(0, 1fr);
  overflow: hidden;
}
.body.with-log {
  grid-template-columns: 300px minmax(0, 1fr) 320px;
}

@media (max-width: 1100px) {
  .body.with-log {
    grid-template-columns: 260px minmax(0, 1fr);
  }
  .body.with-log > :last-child {
    display: none;
  }
}

/* Narrow screens show either the chat list or the open chat. */
@media (max-width: 720px) {
  .body,
  .body.with-log {
    grid-template-columns: minmax(0, 1fr);
  }
  .body.chat-open .sidebar {
    display: none;
  }
  .body:not(.chat-open) .main {
    display: none;
  }
}
.sidebar {
  display: grid;
  grid-template-rows: auto minmax(0, 1fr);
  overflow: hidden;
  border-right: 1px solid var(--border);
}
.search {
  padding: 10px;
  border-bottom: 1px solid var(--border);
}
.search-input {
  width: 100%;
  padding: 7px 10px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface-solid);
}
.search-input:focus {
  border-color: var(--accent);
  outline: none;
}
.panels {
  overflow-y: auto;
}
.main {
  display: grid;
  overflow: hidden;
}
</style>
