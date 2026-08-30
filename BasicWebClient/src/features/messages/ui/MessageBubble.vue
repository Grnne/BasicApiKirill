<script setup lang="ts">
import type { Message } from '@/entities/message/types'
import { formatTime } from '@/shared/lib/date'

defineProps<{
  message: Message
  own: boolean
}>()
</script>

<template>
  <article :class="['bubble', { own }]">
    <span v-if="!own" class="sender">{{ message.senderName }}</span>
    <!-- Текст сообщения — только интерполяция. Никакого v-html здесь быть не может. -->
    <p class="text">{{ message.text }}</p>
    <span class="time">{{ formatTime(message.createdAt) }}</span>
  </article>
</template>

<style scoped>
.bubble {
  max-width: min(560px, 75%);
  align-self: flex-start;
  padding: 7px 11px;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  border-bottom-left-radius: 2px;
  background: var(--surface-solid);
}
.bubble.own {
  align-self: flex-end;
  border-color: transparent;
  border-radius: var(--radius);
  border-bottom-right-radius: 2px;
  background: var(--accent-soft);
}
.sender {
  display: block;
  margin-bottom: 2px;
  color: var(--accent);
  font-size: 12px;
  font-weight: 600;
}
.text {
  margin: 0;
  /* Переносим длинные слова и сохраняем переводы строк из ввода. */
  overflow-wrap: anywhere;
  white-space: pre-wrap;
}
.time {
  display: block;
  margin-top: 2px;
  color: var(--text-faint);
  font-size: 10px;
  text-align: right;
}
</style>
