<script setup lang="ts">
import { ref, watch } from 'vue'

import type { ChatListItem } from '@/entities/chat/types'
import { useChatsStore } from '@/entities/chat/model/chats.store'
import { useDebounced } from '@/shared/lib/useDebounced'
import * as chatsApi from '../api/chats.api'
import { useChatListStore } from '../model/chat-list.store'
import ChatMenu from './ChatMenu.vue'
import ChatRow from './ChatRow.vue'

const props = defineProps<{ query: string }>()

const store = useChatListStore()
const chats = useChatsStore()

/** The chat whose menu is open and where (right click or long press on a row). */
const menu = ref<{ chat: ChatListItem; x: number; y: number } | null>(null)

function openMenu(chat: ChatListItem, event: MouseEvent): void {
  menu.value = { chat, x: event.clientX, y: event.clientY }
}

const found = ref<ChatListItem[]>([])
const isSearching = ref(false)

const debouncedQuery = useDebounced(() => props.query)

/**
 * The previous search is aborted: responses can arrive out of order, and a stale one would
 * otherwise overwrite the results for the current query.
 */
let inFlight: AbortController | null = null

watch(debouncedQuery, async (query) => {
  inFlight?.abort()

  const trimmed = query.trim()
  if (trimmed.length === 0) {
    found.value = []
    isSearching.value = false
    return
  }

  const controller = new AbortController()
  inFlight = controller
  isSearching.value = true

  try {
    const response = await chatsApi.searchChats(trimmed, controller.signal)
    found.value = response.items
  } catch {
    if (!controller.signal.aborted) found.value = []
  } finally {
    if (!controller.signal.aborted) isSearching.value = false
  }
})

</script>

<template>
  <section class="panel">
    <h2 class="heading">{{ query.trim() ? 'Найденные чаты' : 'Чаты' }}</h2>

    <p v-if="!chats.loaded" class="note">загрузка…</p>

    <template v-else-if="query.trim()">
      <p v-if="isSearching" class="note">ищем…</p>
      <p v-else-if="found.length === 0" class="note">ничего не нашлось</p>
      <ChatRow
        v-for="chat in found"
        :key="chat.chatId"
        :chat="chat"
        :active="chat.chatId === store.selectedChatId"
        @click="store.select(chat.chatId)"
      />
    </template>

    <template v-else>
      <p v-if="chats.list.length === 0" class="note">пока ни одного чата</p>
      <ChatRow
        v-for="chat in chats.list"
        :key="chat.chatId"
        :chat="chat"
        :active="chat.chatId === store.selectedChatId"
        @click="store.select(chat.chatId)"
        @contextmenu.prevent="openMenu(chat, $event)"
      />
    </template>

    <ChatMenu v-if="menu" :chat="menu.chat" :x="menu.x" :y="menu.y" @close="menu = null" />
  </section>
</template>

<style scoped>
.panel {
  display: grid;
  align-content: start;
}
.heading {
  margin: 0;
  padding: 10px 12px 6px;
  color: var(--text-faint);
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.7px;
  text-transform: uppercase;
}
.note {
  margin: 0;
  padding: 6px 12px 10px;
  color: var(--text-dim);
  font-size: 12px;
}
.note.error {
  color: var(--danger);
}
</style>
