<script setup lang="ts">
import { defineAsyncComponent, ref } from 'vue'
import { useRouter } from 'vue-router'

import BaseButton from '@/shared/ui/BaseButton.vue'
import ConnectionStatus from '@/features/realtime/ui/ConnectionStatus.vue'
import ChatListPanel from '@/features/chat-list/ui/ChatListPanel.vue'
import FolderTabs from '@/features/chat-list/ui/FolderTabs.vue'
import ChatWindow from '@/features/messages/ui/ChatWindow.vue'
import ChatInfoPanel from '@/features/chat-info/ui/ChatInfoPanel.vue'
import AddMembersDialog from '@/features/groups/ui/AddMembersDialog.vue'
import CreateGroupDialog from '@/features/groups/ui/CreateGroupDialog.vue'
import { useNoticesStore } from '@/shared/ui/notices.store'
import UserSearchPanel from '@/features/user-search/ui/UserSearchPanel.vue'
import MessageSearchPanel from '@/features/message-search/ui/MessageSearchPanel.vue'
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

/** A found message: its chat opens at it (or the open chat scrolls to it). */
async function onMessageFound(chatId: string, messageId: string): Promise<void> {
  if (chatList.selectedChatId === chatId) {
    await messages.jumpTo(messageId)
    return
  }
  messages.requestJump(chatId, messageId)
  await chatList.select(chatId)
}

const notices = useNoticesStore()
const creatingGroup = ref(false)
const addingMembers = ref(false)

function onMembersAdded(count: number): void {
  addingMembers.value = false
  notices.push(count > 0 ? `Добавлено участников: ${count}` : 'Все выбранные уже в группе', 'info')
}

/** Left on purpose: the chat is closed before MemberRemoved takes it from the list. */
async function onLeftGroup(): Promise<void> {
  infoOpen.value = false
  await chatList.deselect()
}

async function onGroupCreated(chatId: string): Promise<void> {
  creatingGroup.value = false
  query.value = ''
  await chatList.select(chatId)
}

// The chat card stays open while the user goes from chat to chat.
const infoOpen = ref(false)

/** A file or a link of the card: the chat scrolls to its message; a narrow screen shows the chat. */
async function onInfoJump(messageId: string): Promise<void> {
  if (window.matchMedia('(max-width: 1000px)').matches) infoOpen.value = false
  await messages.jumpTo(messageId)
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
      <BaseButton variant="ghost" title="Новая группа" @click="creatingGroup = true">＋</BaseButton>
      <BaseButton variant="ghost" title="Избранное" @click="chatList.openSaved()">★</BaseButton>
      <BaseButton variant="ghost" @click="onLogout">Выйти</BaseButton>
    </header>

    <div :class="['body', { 'with-log': isDev, 'chat-open': chatList.selectedChatId !== null }]">
      <aside class="sidebar">
        <div class="search">
          <input
            v-model="query"
            class="search-input"
            type="search"
            placeholder="Поиск чатов, людей и сообщений"
            autocomplete="off"
          />
        </div>
        <FolderTabs v-if="!query.trim()" />
        <div class="panels">
          <ChatListPanel :query="query" />
          <UserSearchPanel :query="query" @select="onUserSelected" />
          <MessageSearchPanel :query="query" @open="onMessageFound" />
        </div>
      </aside>

      <main class="main">
        <ChatWindow @info="infoOpen = !infoOpen" />
        <ChatInfoPanel
          v-if="infoOpen && chatList.selectedChat"
          class="info-panel"
          :chat="chatList.selectedChat"
          :me-id="auth.user?.userId ?? null"
          @close="infoOpen = false"
          @jump="onInfoJump"
          @add-members="addingMembers = true"
          @open-user="onUserSelected"
          @left="onLeftGroup"
        />
      </main>

      <component :is="EventLog" v-if="EventLog" />
    </div>

    <CreateGroupDialog v-if="creatingGroup" @created="onGroupCreated" @cancel="creatingGroup = false" />
    <AddMembersDialog
      v-if="addingMembers && chatList.selectedChatId && auth.user"
      :chat-id="chatList.selectedChatId"
      :me-id="auth.user.userId"
      @done="onMembersAdded"
      @cancel="addingMembers = false"
    />
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
  display: flex;
  flex-direction: column;
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
  flex: 1;
  min-height: 0;
  overflow-y: auto;
}
.main {
  position: relative;
  display: grid;
  grid-auto-flow: column;
  grid-template-columns: minmax(0, 1fr);
  overflow: hidden;
}
.info-panel {
  width: 320px;
}
/* No room beside the chat: the card lies over it. */
@media (max-width: 1000px) {
  .info-panel {
    position: absolute;
    inset: 0 0 0 auto;
    z-index: 20;
    width: min(340px, 100%);
    box-shadow: -4px 0 16px #0006;
  }
}
</style>
