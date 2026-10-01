<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'

import * as chatApi from '@/entities/chat/api'
import { chatTitle } from '@/entities/chat/lib'
import { useChatsStore } from '@/entities/chat/model/chats.store'
import { useRestoreFocus } from '@/shared/ui/useRestoreFocus'

defineProps<{ count: number }>()
const emit = defineEmits<{ pick: [chatId: string]; cancel: [] }>()
useRestoreFocus()

const chats = useChatsStore()
const query = ref('')
const search = ref<HTMLInputElement | null>(null)

const found = computed(() => {
  const q = query.value.trim().toLowerCase()
  const list = chats.list.filter((c) => !c.archived && c.type !== 'saved')
  return q ? list.filter((c) => chatTitle(c).toLowerCase().includes(q)) : list
})

/** "Saved messages" is always offered first, created if it does not exist yet. */
async function pickSaved(): Promise<void> {
  const existing = chats.list.find((c) => c.type === 'saved')
  if (existing) {
    emit('pick', existing.chatId)
    return
  }
  try {
    const saved = await chatApi.openSavedChat()
    chats.put(saved)
    emit('pick', saved.chatId)
  } catch {
    emit('cancel')
  }
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') emit('cancel')
}
onMounted(() => {
  document.addEventListener('keydown', onKeydown)
  search.value?.focus()
})
onUnmounted(() => document.removeEventListener('keydown', onKeydown))
</script>

<template>
  <div class="overlay" @click.self="emit('cancel')">
    <div class="dialog" role="dialog" aria-modal="true" aria-label="Переслать">
      <h2 class="title">Переслать {{ count > 1 ? `сообщения (${count})` : 'сообщение' }}</h2>
      <input ref="search" v-model="query" class="search" type="search" placeholder="Найти чат" />
      <ul class="list">
        <li v-if="!query.trim()">
          <button type="button" class="chat" @click="pickSaved">★ Избранное</button>
        </li>
        <li v-for="chat in found" :key="chat.chatId">
          <button type="button" class="chat" @click="emit('pick', chat.chatId)">{{ chatTitle(chat) }}</button>
        </li>
        <li v-if="found.length === 0 && query.trim()" class="empty">Ничего не нашлось</li>
      </ul>
      <button type="button" class="cancel" @click="emit('cancel')">Отмена</button>
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
  display: grid;
  gap: 10px;
  width: min(380px, 100%);
  max-height: min(560px, 90vh);
  padding: 18px;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--surface-solid);
}
.title {
  margin: 0;
  font-size: 16px;
}
.search {
  padding: 7px 10px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--bg);
}
.list {
  overflow-y: auto;
  margin: 0;
  padding: 0;
  list-style: none;
}
.chat {
  display: block;
  width: 100%;
  padding: 8px 10px;
  border: none;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text);
  text-align: left;
}
.chat:hover {
  background: var(--surface-hover);
}
.empty {
  padding: 8px 10px;
  color: var(--text-dim);
}
.cancel {
  justify-self: end;
  padding: 7px 14px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text);
}
</style>
