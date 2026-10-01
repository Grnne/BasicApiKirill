<script setup lang="ts">
import { defineAsyncComponent, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import BaseButton from '@/shared/ui/BaseButton.vue'
import ConnectionStatus from '@/features/realtime/ui/ConnectionStatus.vue'
import ChatListPanel from '@/features/chat-list/ui/ChatListPanel.vue'
import FolderTabs from '@/features/chat-list/ui/FolderTabs.vue'
import ChatWindow from '@/features/messages/ui/ChatWindow.vue'
import ChatInfoPanel from '@/features/chat-info/ui/ChatInfoPanel.vue'
import AddMembersDialog from '@/features/groups/ui/AddMembersDialog.vue'
import AuditLogDialog from '@/features/groups/ui/AuditLogDialog.vue'
import GroupEditDialog from '@/features/groups/ui/GroupEditDialog.vue'
import MemberDialog from '@/features/groups/ui/MemberDialog.vue'
import CreateGroupDialog from '@/features/groups/ui/CreateGroupDialog.vue'
import { useNoticesStore } from '@/shared/ui/notices.store'
import { useChatDialogs } from './lib/useChatDialogs'
import { useLogout } from './lib/useLogout'
import UserSearchPanel from '@/features/user-search/ui/UserSearchPanel.vue'
import MessageSearchPanel from '@/features/message-search/ui/MessageSearchPanel.vue'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { useChatListStore } from '@/features/chat-list/model/chat-list.store'
import { useMessagesStore } from '@/features/messages/model/messages.store'
import { useAccountStore } from '@/entities/user/model/account.store'
import { useChatsStore } from '@/entities/chat/model/chats.store'
import AvatarCircle from '@/entities/media/ui/AvatarCircle.vue'

const auth = useAuthStore()
const chatList = useChatListStore()
const messages = useMessagesStore()
const account = useAccountStore()
const router = useRouter()
const route = useRoute()
const chats = useChatsStore()

// ?open=<chatId> — from a notification: the chat opens once the list has it.
watch(
  () => [route.query.open, chats.loaded] as const,
  ([open, loaded]) => {
    if (typeof open !== 'string' || !loaded) return
    if (chats.get(open)) void chatList.select(open)
    void router.replace({ query: {} })
  },
  { immediate: true },
)

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
const { addingMembers, editingGroup, managedMemberId, showingAudit } = useChatDialogs(() => chatList.selectedChatId)

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

const { logout: onLogout } = useLogout()
</script>

<template>
  <div class="page">
    <header class="bar">
      <span class="brand">Basic<span class="accent">Chat</span></span>
      <ConnectionStatus />
      <button type="button" class="user" title="Настройки" @click="router.push({ name: 'settings' })">
        <AvatarCircle
          :avatar-id="account.me?.avatarId ?? null"
          :initial="(account.me?.displayName ?? auth.user?.displayName ?? '?').charAt(0).toUpperCase()"
          :size="26"
        />
        <span class="user-name">{{ account.me?.displayName ?? auth.user?.displayName }}</span>
      </button>
      <BaseButton variant="ghost" title="Новая группа" aria-label="Новая группа" @click="creatingGroup = true">＋</BaseButton>
      <BaseButton variant="ghost" title="Избранное" aria-label="Избранное" @click="chatList.openSaved()">★</BaseButton>
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
          @leaving="chatList.expectGone(chatList.selectedChatId!)"
          @left="onLeftGroup"
          @edit-group="editingGroup = true"
          @manage-member="managedMemberId = $event"
          @audit="showingAudit = true"
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
    <template v-if="chatList.selectedChatId && auth.user">
      <GroupEditDialog
        v-if="editingGroup"
        :chat-id="chatList.selectedChatId"
        :me-id="auth.user.userId"
        @done="editingGroup = false"
        @deleting="chatList.expectGone(chatList.selectedChatId!)"
        @deleted="editingGroup = false; onLeftGroup()"
        @cancel="editingGroup = false"
      />
      <MemberDialog
        v-if="managedMemberId"
        :chat-id="chatList.selectedChatId"
        :me-id="auth.user.userId"
        :user-id="managedMemberId"
        @close="managedMemberId = null"
      />
      <AuditLogDialog v-if="showingAudit" :chat-id="chatList.selectedChatId" @close="showingAudit = false" />
    </template>
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
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
  margin-left: auto;
  padding: 2px 8px 2px 2px;
  border: none;
  border-radius: 16px;
  background: none;
  color: var(--text-dim);
}
.user:hover {
  background: var(--surface-hover);
  color: var(--text);
}
.user-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
/* A phone has room for the buttons only: the avatar stands for the name. */
@media (max-width: 720px) {
  .bar {
    gap: 6px;
    padding: 8px 10px;
  }
  .brand,
  .user-name {
    display: none;
  }
  .bar :deep(.btn) {
    padding: 8px 10px;
  }
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
