<script setup lang="ts">
import { computed, ref, watch } from 'vue'

import { chatInitial, chatTitle } from '@/entities/chat/lib'
import AvatarCircle from '@/entities/media/ui/AvatarCircle.vue'
import { usePresenceStore } from '@/entities/user/presence.store'
import { useChatListStore } from '@/features/chat-list/model/chat-list.store'
import { useMessagesStore } from '../model/messages.store'
import ChatSearch from './ChatSearch.vue'
import MessageList from './MessageList.vue'
import MessageComposer from './MessageComposer.vue'
import SelectionBar from './SelectionBar.vue'

const chatList = useChatListStore()
const messages = useMessagesStore()
const presence = usePresenceStore()

// Typing takes precedence over the online status.
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

const searching = ref(false)

// One-way link: the chat list knows nothing about messages, which follow the selected chatId.
watch(
  () => chatList.selectedChatId,
  (chatId) => {
    searching.value = false
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
      <button type="button" class="back" title="К списку чатов" @click="chatList.deselect()">
        ←
      </button>
      <AvatarCircle :avatar-id="chatList.selectedChat.avatarId" :initial="chatInitial(chatList.selectedChat)" :size="32" />
      <span class="title">{{ chatTitle(chatList.selectedChat) }}</span>
      <span :class="['subtitle', { typing: isTyping }]">{{ subtitle }}</span>
      <button type="button" class="search-toggle" title="Поиск в чате" @click="searching = !searching">🔍</button>
    </header>

    <ChatSearch v-if="searching" @close="searching = false" />

    <MessageList />
    <SelectionBar v-if="messages.selected.size > 0" />
    <MessageComposer v-else />
  </section>

  <section v-else class="empty">
    <p class="hint">Выбери чат слева или найди человека через поиск.</p>
  </section>
</template>

<style scoped>
.window {
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
/* The message list (a child's root gets this scope) takes the height that is left. */
.window > .viewport {
  flex: 1;
  min-height: 0;
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
.search-toggle {
  margin-left: auto;
  padding: 2px 6px;
  border: none;
  background: none;
  color: var(--text-dim);
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
