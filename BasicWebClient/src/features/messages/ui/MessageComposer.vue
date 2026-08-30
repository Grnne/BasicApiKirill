<script setup lang="ts">
import { computed, onUnmounted, ref } from 'vue'

import { useHubStore } from '@/shared/api/hub.store'
import { useMessagesStore } from '../model/messages.store'

/** Сервер режет длинный текст в превью списка; здесь просто разумный предел. */
const MAX_LENGTH = 4000

/**
 * «Печатает» отправляем не чаще раза в 3 секунды: на хабе стоит лимит вызовов,
 * и тратить его на каждую букву — верный способ упереться в него при отправке.
 */
const TYPING_THROTTLE_MS = 3_000

/** Через столько после последнего нажатия сообщаем, что печатать перестали. */
const TYPING_STOP_DELAY_MS = 3_000

const hub = useHubStore()
const store = useMessagesStore()

const text = ref('')
const errorText = ref('')

const canSend = computed(
  () => hub.status === 'connected' && text.value.trim().length > 0 && !isSending.value,
)

const isSending = ref(false)

let lastTypingSentAt = 0
let stopTypingTimer: ReturnType<typeof setTimeout> | undefined

function stopTyping(): void {
  clearTimeout(stopTypingTimer)
  stopTypingTimer = undefined

  const chatId = store.chatId
  if (chatId && lastTypingSentAt > 0) {
    lastTypingSentAt = 0
    void hub.sendTyping(chatId, false)
  }
}

function onInput(): void {
  const chatId = store.chatId
  if (!chatId) return

  const now = Date.now()
  if (now - lastTypingSentAt > TYPING_THROTTLE_MS) {
    lastTypingSentAt = now
    void hub.sendTyping(chatId, true)
  }

  // Явное «перестал печатать» — иначе у собеседника индикатор провисит
  // до истечения таймаута.
  clearTimeout(stopTypingTimer)
  stopTypingTimer = setTimeout(stopTyping, TYPING_STOP_DELAY_MS)
}

// Уходя со страницы, не оставляем собеседнику вечное «печатает…».
onUnmounted(stopTyping)

async function submit(): Promise<void> {
  if (!canSend.value) return

  isSending.value = true
  errorText.value = ''

  const value = text.value
  // Очищаем поле сразу — иначе быстрый набор следующего сообщения затрётся.
  text.value = ''
  stopTyping()

  const ok = await store.send(value)
  if (!ok) {
    // Не потеряли текст: возвращаем в поле, чтобы можно было отправить снова.
    text.value = value
    errorText.value = 'Сообщение не ушло. Проверь связь и попробуй ещё раз.'
  }

  isSending.value = false
}

/** Enter отправляет, Shift+Enter переносит строку — привычно по мессенджерам. */
function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault()
    void submit()
  }
}
</script>

<template>
  <form class="composer" @submit.prevent="submit">
    <textarea
      v-model="text"
      class="input"
      rows="1"
      :maxlength="MAX_LENGTH"
      :placeholder="hub.status === 'connected' ? 'Написать сообщение' : 'Нет связи с сервером'"
      :disabled="hub.status !== 'connected'"
      @input="onInput"
      @keydown="onKeydown"
    />
    <button type="submit" class="send" :disabled="!canSend">Отправить</button>
    <p v-if="errorText" class="error" role="alert">{{ errorText }}</p>
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
.error {
  grid-column: 1 / -1;
  margin: 0;
  color: var(--danger);
  font-size: 12px;
}
</style>
