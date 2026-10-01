<script setup lang="ts">
import { ref, watch } from 'vue'

import type { GlobalSearchHitDto } from '@/shared/api/schema'
import { describeError } from '@/shared/api/problem'
import { formatDay } from '@/shared/lib/date'
import { useDebounced } from '@/shared/lib/useDebounced'
import * as searchApi from '../api/search.api'

const MIN_QUERY = 2

const props = defineProps<{ query: string }>()
const emit = defineEmits<{ open: [chatId: string, messageId: string] }>()

const hits = ref<GlobalSearchHitDto[]>([])
const cursor = ref<string | null>(null)
const busy = ref(false)
const error = ref('')

const debounced = useDebounced(() => props.query.trim(), 300)

/** A new query aborts the previous one: an older answer must not replace a newer one. */
let inFlight: AbortController | null = null

async function search(q: string, more = false): Promise<void> {
  inFlight?.abort()
  if (q.length < MIN_QUERY) {
    hits.value = []
    cursor.value = null
    return
  }
  const controller = new AbortController()
  inFlight = controller
  busy.value = true
  error.value = ''
  try {
    const page = await searchApi.searchMessages(q, more ? cursor.value : null, controller.signal)
    hits.value = more ? [...hits.value, ...page.items] : page.items
    cursor.value = page.hasMore ? page.nextCursor : null
  } catch (e) {
    if (!controller.signal.aborted) error.value = describeError(e)
  } finally {
    if (!controller.signal.aborted) busy.value = false
  }
}

watch(debounced, (q) => void search(q), { immediate: true })

/** A hit's chat as the user knows it: the companion's name or the group's title. */
function chatName(hit: GlobalSearchHitDto): string {
  if (hit.chat.type === 'saved') return 'Избранное'
  return hit.chat.companionName ?? hit.chat.title ?? 'Без названия'
}
</script>

<template>
  <section v-if="debounced.length >= MIN_QUERY" class="panel">
    <h2 class="heading">Сообщения</h2>
    <p v-if="error" class="note error">{{ error }}</p>
    <p v-else-if="busy && hits.length === 0" class="note">ищем…</p>
    <p v-else-if="hits.length === 0" class="note">ничего не нашлось</p>

    <button
      v-for="hit in hits"
      :key="hit.message.id"
      type="button"
      class="hit"
      @click="emit('open', hit.chat.chatId, hit.message.id)"
    >
      <span class="chat">{{ chatName(hit) }}</span>
      <span class="when">{{ formatDay(hit.message.createdAt) }}</span>
      <span class="text">{{ hit.message.senderName }}: {{ hit.message.text }}</span>
    </button>
    <button v-if="cursor" type="button" class="more" :disabled="busy" @click="search(debounced, true)">Ещё</button>
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
.hit {
  display: grid;
  grid-template-columns: 1fr auto;
  gap: 2px 8px;
  padding: 7px 12px;
  border: none;
  background: transparent;
  color: var(--text);
  text-align: left;
}
.hit:hover {
  background: var(--surface-hover);
}
.chat {
  overflow: hidden;
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.when {
  color: var(--text-faint);
  font-size: 11px;
}
.text {
  grid-column: 1 / -1;
  overflow: hidden;
  color: var(--text-dim);
  font-size: 12px;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.more {
  padding: 6px;
  border: none;
  background: transparent;
  color: var(--accent);
}
</style>
