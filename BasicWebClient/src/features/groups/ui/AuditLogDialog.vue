<script setup lang="ts">
import { onMounted, onUnmounted, ref, shallowRef } from 'vue'

import * as chatApi from '@/entities/chat/api'
import { useChatDetailsStore } from '@/entities/chat/model/details.store'
import { describeError } from '@/shared/api/problem'
import type { AuditEntryDto } from '@/shared/api/schema'
import { formatDay, formatTime } from '@/shared/lib/date'
import { auditText } from '../lib/audit'

const props = defineProps<{ chatId: string }>()
const emit = defineEmits<{ close: [] }>()

const details = useChatDetailsStore()
const nameOf = (userId: string) => details.member(props.chatId, userId)?.displayName ?? null

const entries = shallowRef<AuditEntryDto[]>([])
const cursor = ref<string | null>(null)
const done = ref(false)
const busy = ref(false)
const error = ref<string | null>(null)

async function load(): Promise<void> {
  if (busy.value || done.value) return
  busy.value = true
  error.value = null
  try {
    const page = await chatApi.getAudit(props.chatId, cursor.value)
    entries.value = [...entries.value, ...page.items]
    cursor.value = page.nextCursor
    done.value = page.nextCursor === null
  } catch (e) {
    error.value = describeError(e)
  } finally {
    busy.value = false
  }
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') emit('close')
}
onMounted(() => {
  document.addEventListener('keydown', onKeydown)
  void load()
})
onUnmounted(() => document.removeEventListener('keydown', onKeydown))
</script>

<template>
  <div class="overlay" @click.self="emit('close')">
    <div class="dialog" role="dialog" aria-modal="true" aria-label="Журнал действий">
      <header class="head">
        <h2 class="heading">Журнал действий</h2>
        <button type="button" class="close" title="Закрыть" @click="emit('close')">✕</button>
      </header>

      <ol class="list">
        <li v-for="e in entries" :key="e.id" class="entry">
          <span class="text">{{ auditText(e, nameOf) }}</span>
          <span class="when">{{ formatDay(e.createdAt) }} {{ formatTime(e.createdAt) }}</span>
        </li>
      </ol>

      <p v-if="error" class="note error">{{ error }}</p>
      <p v-else-if="busy" class="note">загрузка…</p>
      <p v-else-if="entries.length === 0" class="note">Записей нет</p>
      <button v-else-if="!done" type="button" class="more" @click="load">Ещё</button>
    </div>
  </div>
</template>

<style scoped>
.overlay {
  position: fixed;
  inset: 0;
  z-index: 90;
  display: grid;
  place-items: center;
  padding: 16px;
  background: #000a;
}
.dialog {
  display: flex;
  flex-direction: column;
  width: min(480px, 100%);
  max-height: calc(100vh - 32px);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--surface-solid);
}
.head {
  display: flex;
  align-items: center;
  padding: 14px 18px;
  border-bottom: 1px solid var(--border);
}
.heading {
  margin: 0;
  font-size: 16px;
}
.close {
  margin-left: auto;
  border: none;
  background: none;
  color: var(--text-dim);
}
.list {
  flex: 1;
  min-height: 0;
  margin: 0;
  padding: 0;
  overflow-y: auto;
  list-style: none;
}
.entry {
  display: grid;
  gap: 2px;
  padding: 8px 18px;
  border-bottom: 1px solid var(--border);
}
.text {
  overflow-wrap: anywhere;
}
.when {
  color: var(--text-faint);
  font-size: 11px;
}
.note {
  margin: 0;
  padding: 12px 18px;
  color: var(--text-dim);
  font-size: 12px;
}
.note.error {
  color: var(--danger);
}
.more {
  padding: 10px;
  border: none;
  background: none;
  color: var(--accent);
}
</style>
