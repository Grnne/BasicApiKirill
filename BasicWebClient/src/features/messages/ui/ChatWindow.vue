<script setup lang="ts">
import { computed, onMounted, watch } from 'vue'

import { chatTitle } from '@/entities/chat/lib'
import { usePresenceStore } from '@/entities/user/presence.store'
import { useChatListStore } from '@/features/chat-list/model/chat-list.store'
import { useMessagesStore } from '../model/messages.store'
import MessageList from './MessageList.vue'
import MessageComposer from './MessageComposer.vue'

const chatList = useChatListStore()
const messages = useMessagesStore()
const presence = usePresenceStore()

/** Подпись под названием чата: «печатает…» важнее, чем «в сети». */
const subtitle = computed(() => {
  const chat = chatList.selectedChat
  if (!chat) return ''

  if (presence.isSomeoneTyping(chat.chatId)) return 'печатает…'
  if (chat.type !== 'private') return ''
  return presence.isOnline(chat.companionId) ? 'в сети' : 'не в сети'
})

const isTyping = computed(
  () => chatList.selectedChat !== null && presence.isSomeoneTyping(chatList.selectedChat.chatId),
)

onMounted(() => {
  messages.subscribeToHub()
})

/**
 * Лента следует за выбором в списке чатов. Связь односторонняя: список ничего
 * не знает про сообщения, а сообщения только читают выбранный chatId.
 */
watch(
  () => chatList.selectedChatId,
  (chatId) => {
    if (chatId) {
      void messages.openChat(chatId)
    } else {
      messages.reset()
    }
  },
  { immediate: true },
)
</script>

<template>
  <section v-if="chatList.selectedChat" class="window">
    <header class="head">
      <!-- Видна только на узких экранах: там список и переписка не помещаются рядом. -->
      <button type="button" class="back" title="К списку чатов" @click="chatList.deselect()">
        ←
      </button>
      <span class="title">{{ chatTitle(chatList.selectedChat) }}</span>
      <span :class="['subtitle', { typing: isTyping }]">{{ subtitle }}</span>
    </header>

    <MessageList />
    <MessageComposer />
  </section>

  <section v-else class="empty">
    <p class="hint">Выбери чат слева или найди человека через поиск.</p>
  </section>
</template>

<style scoped>
.window {
  display: grid;
  grid-template-rows: auto minmax(0, 1fr) auto;
  overflow: hidden;
}
.head {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 11px 16px;
  border-bottom: 1px solid var(--border);
  background: var(--surface-solid);
}
.back {
  display: none;
  padding: 2px 8px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-dim);
}
@media (max-width: 720px) {
  .back {
    display: block;
  }
}
.title {
  font-weight: 600;
}
.subtitle {
  color: var(--text-dim);
  font-size: 12px;
}
.subtitle.typing {
  color: var(--accent);
  font-style: italic;
}
.empty {
  display: grid;
  place-content: center;
  padding: 20px;
}
.hint {
  color: var(--text-dim);
  text-align: center;
}
</style>
