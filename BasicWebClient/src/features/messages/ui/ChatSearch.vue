<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'

import type { MessageDto } from '@/shared/api/schema'
import { describeError } from '@/shared/api/problem'
import { formatDay, formatTime } from '@/shared/lib/date'
import { useDebounced } from '@/shared/lib/useDebounced'
import * as messagesApi from '../api/messages.api'
import { useMessagesStore } from '../model/messages.store'

const MIN_QUERY = 2

const emit = defineEmits<{ close: [] }>()

const store = useMessagesStore()

const query = ref('')
const date = ref('')
const results = ref<MessageDto[]>([])
const total = ref(0)
const cursor = ref<string | null>(null)
const busy = ref(false)
const error = ref('')
const input = ref<HTMLInputElement | null>(null)

const debounced = useDebounced(() => query.value.trim(), 300)

/** A new query aborts the previous one: an older answer must not replace a newer one. */
let inFlight: AbortController | null = null

async function search(q: string, more = false): Promise<void> {
  const chatId = store.chatId
  inFlight?.abort()
  if (!chatId || q.length < MIN_QUERY) {
    results.value = []
    total.value = 0
    cursor.value = null
    return
  }
  const controller = new AbortController()
  inFlight = controller
  busy.value = true
  error.value = ''
  try {
    const page = await messagesApi.searchInChat(chatId, q, more ? cursor.value : null, controller.signal)
    results.value = more ? [...results.value, ...page.items] : page.items
    total.value = page.totalCount
    cursor.value = page.hasMore ? page.nextCursor : null
  } catch (e) {
    if (!controller.signal.aborted) error.value = describeError(e)
  } finally {
    if (!controller.signal.aborted) busy.value = false
  }
}

watch(debounced, (q) => void search(q))

function goToDate(): void {
  if (!date.value) return
  // The end of the chosen day, local time: the last message of that day comes into view.
  const [y, m, d] = date.value.split('-').map(Number)
  void store.jumpToDate(new Date(y!, m! - 1, d!, 23, 59, 59))
}

onMounted(() => input.value?.focus())
</script>

<template>
  <div class="search">
    <div class="bar">
      <input
        ref="input"
        v-model="query"
        class="field"
        type="search"
        placeholder="Поиск в чате"
        @keydown.esc="emit('close')"
      />
      <input v-model="date" class="date" type="date" title="Перейти к дате" @change="goToDate" />
      <button type="button" class="close" title="Закрыть" @click="emit('close')">✕</button>
    </div>

    <p v-if="error" class="note error">{{ error }}</p>
    <p v-else-if="debounced.length >= MIN_QUERY && !busy && results.length === 0" class="note">ничего не нашлось</p>
    <p v-else-if="results.length > 0" class="note">Найдено: {{ total }}</p>

    <ul v-if="results.length > 0" class="results">
      <li v-for="message in results" :key="message.id">
        <button type="button" class="hit" @click="store.jumpTo(message.id)">
          <span class="who">{{ message.senderName }}</span>
          <span class="when">{{ formatDay(message.createdAt) }} {{ formatTime(message.createdAt) }}</span>
          <span class="text">{{ message.text }}</span>
        </button>
      </li>
      <li v-if="cursor">
        <button type="button" class="more" :disabled="busy" @click="search(debounced, true)">Ещё</button>
      </li>
    </ul>
  </div>
</template>

<style scoped>
.search {
  border-bottom: 1px solid var(--border);
  background: var(--surface-solid);
}
.bar {
  display: flex;
  gap: 6px;
  padding: 8px 12px;
}
.field {
  flex: 1;
  padding: 6px 10px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--bg);
}
.date {
  padding: 4px 6px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--bg);
  color: var(--text-dim);
  color-scheme: dark;
}
.close {
  border: none;
  background: none;
  color: var(--text-dim);
}
.note {
  margin: 0;
  padding: 0 12px 6px;
  color: var(--text-dim);
  font-size: 12px;
}
.note.error {
  color: var(--danger);
}
.results {
  overflow-y: auto;
  max-height: 240px;
  margin: 0;
  padding: 0 4px 6px;
  list-style: none;
}
.hit {
  display: grid;
  grid-template-columns: 1fr auto;
  gap: 0 8px;
  width: 100%;
  padding: 6px 8px;
  border: none;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text);
  text-align: left;
}
.hit:hover {
  background: var(--surface-hover);
}
.who {
  color: var(--accent);
  font-size: 12px;
  font-weight: 600;
}
.when {
  color: var(--text-faint);
  font-size: 11px;
}
.text {
  grid-column: 1 / -1;
  overflow: hidden;
  color: var(--text-dim);
  font-size: 13px;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.more {
  width: 100%;
  padding: 6px;
  border: none;
  background: transparent;
  color: var(--accent);
}
</style>
