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

// Запрос уходит через паузу после последней буквы, а не на каждое нажатие.
const debouncedQuery = useDebounced(() => props.query)

/**
 * Прошлый поиск отменяем: ответы возвращаются не в том порядке, в каком
 * ушли запросы, и без отмены на экране может остаться выдача по «ба»,
 * когда в поле уже «банан».
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
    // Отмена — обычное дело, показывать нечего.
    if (!controller.signal.aborted) found.value = []
  } finally {
    if (!controller.signal.aborted) isSearching.value = false
  }
})

onMounted(async () => {
  store.subscribeToHub()
  presence.subscribeToHub()

  await store.load()

  // Одним запросом спрашиваем статусы всех собеседников из списка: события
  // UserOnlineChanged, случившиеся до нашего подключения, мы не видели.
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
