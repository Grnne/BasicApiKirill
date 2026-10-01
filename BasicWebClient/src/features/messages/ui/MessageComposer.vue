<script setup lang="ts">
import { computed, nextTick, onUnmounted, ref, watch } from 'vue'

import { useConfigStore } from '@/entities/config/config.store'
import * as messagesApi from '../api/messages.api'
import { useMessagesStore } from '../model/messages.store'

/**
 * Typing shares the per-user command limit with sending (20 per 10 s): repeat it only while
 * typing goes on; the server drops it after 6 s without a repeat.
 */
const TYPING_THROTTLE_MS = 3_000

const TYPING_STOP_DELAY_MS = 3_000

const store = useMessagesStore()
const config = useConfigStore()

const text = ref('')
const input = ref<HTMLTextAreaElement | null>(null)

// Editing puts the message into the field; the draft typed before comes back after.
let draftBeforeEdit = ''
watch(
  () => store.editing,
  async (message, previous) => {
    if (message) {
      if (!previous) draftBeforeEdit = text.value
      text.value = message.text
      await nextTick()
      input.value?.focus()
    } else if (previous) {
      text.value = draftBeforeEdit
      draftBeforeEdit = ''
    }
  },
)
const maxLength = computed(() => config.config.messages.maxLength)
const canSend = computed(() => text.value.trim().length > 0)

let lastTypingSentAt = 0
let stopTypingTimer: ReturnType<typeof setTimeout> | undefined

function typing(chatId: string, isTyping: boolean): void {
  messagesApi.sendTyping(chatId, isTyping).catch(() => {
    // Typing is cosmetic: a lost one is not worth an error.
  })
}

function stopTyping(): void {
  clearTimeout(stopTypingTimer)
  stopTypingTimer = undefined

  const chatId = store.chatId
  if (chatId && lastTypingSentAt > 0) {
    lastTypingSentAt = 0
    typing(chatId, false)
  }
}

function onInput(): void {
  const chatId = store.chatId
  if (!chatId) return

  const now = Date.now()
  if (now - lastTypingSentAt > TYPING_THROTTLE_MS) {
    lastTypingSentAt = now
    typing(chatId, true)
  }

  clearTimeout(stopTypingTimer)
  stopTypingTimer = setTimeout(stopTyping, TYPING_STOP_DELAY_MS)
}

onUnmounted(stopTyping)

async function submit(): Promise<void> {
  if (!canSend.value) return
  if (store.editing) {
    await store.saveEdit(text.value)
    return
  }
  // Sending failures show on the message itself, with a retry.
  if (store.send(text.value)) {
    text.value = ''
    stopTyping()
  }
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault()
    void submit()
  } else if (event.key === 'Escape' && store.editing) {
    store.cancelEdit()
  }
}
</script>

<template>
  <form class="composer" @submit.prevent="submit">
    <div v-if="store.editing" class="context">
      <span class="label">Редактирование</span>
      <span class="quote">{{ store.editing.text }}</span>
      <button type="button" class="close" title="Отменить (Esc)" @click="store.cancelEdit()">✕</button>
    </div>
    <textarea
      ref="input"
      v-model="text"
      class="input"
      rows="1"
      :maxlength="maxLength"
      placeholder="Написать сообщение"
      @input="onInput"
      @keydown="onKeydown"
    />
    <button type="submit" class="send" :disabled="!canSend">
      {{ store.editing ? 'Сохранить' : 'Отправить' }}
    </button>
  </form>
</template>

<style scoped>
.composer {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 8px;
  padding: 10px 12px;
  border-top: 1px solid var(--border);
  background: var(--surface-solid);
}
.context {
  grid-column: 1 / -1;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 4px 8px;
  border-left: 2px solid var(--accent);
  background: var(--surface-hover);
  font-size: 12px;
}
.label {
  color: var(--accent);
  font-weight: 600;
}
.quote {
  overflow: hidden;
  flex: 1;
  color: var(--text-dim);
  text-overflow: ellipsis;
  white-space: nowrap;
}
.close {
  border: none;
  background: none;
  color: var(--text-dim);
}
.input {
  min-height: 38px;
  max-height: 140px;
  padding: 9px 11px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--bg);
  resize: vertical;
}
.input:focus {
  border-color: var(--accent);
  outline: none;
}
.input:disabled {
  opacity: 0.5;
}
.send {
  padding: 0 16px;
  border: none;
  border-radius: var(--radius-sm);
  background: var(--accent);
  color: #04160b;
  font-weight: 600;
}
.send:disabled {
  background: var(--surface-hover);
  color: var(--text-faint);
  cursor: default;
}
</style>
