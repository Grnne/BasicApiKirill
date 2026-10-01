<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'

import type { ChatListItem } from '@/entities/chat/types'
import { usePresenceStore } from '@/entities/user/presence.store'
import { useDebounced } from '@/shared/lib/useDebounced'
import * as chatsApi from '../api/chats.api'
import { useChatListStore } from '../model/chat-list.store'
import ChatRow from './ChatRow.vue'

const props = defineProps<{ query: string }>()

const store = useChatListStore()
const presence = usePresenceStore()

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

onMounted(async () => {
  store.subscribeToHub()
  presence.subscribeToHub()

  await store.load()

  // UserOnlineChanged events from before we connected were missed, so fetch the current statuses.
  const companionIds = store.chats
    .map((chat) => chat.companionId)
    .filter((id): id is string => id !== null)

  await Promise.all([presence.loadStatuses(companionIds), presence.loadTyping()])
})
</script>

<template>
  <section class="panel">
    <h2 class="heading">{{ query.trim() ? 'Найденные чаты' : 'Чаты' }}</h2>

    <p v-if="store.isLoading" class="note">загрузка…</p>
    <p v-else-if="store.loadError" class="note error">{{ store.loadError }}</p>

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
      <p v-if="store.chats.length === 0" class="note">пока ни одного чата</p>
      <ChatRow
        v-for="chat in store.chats"
        :key="chat.chatId"
        :chat="chat"
        :active="chat.chatId === store.selectedChatId"
        @click="store.select(chat.chatId)"
      />
    </template>
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
