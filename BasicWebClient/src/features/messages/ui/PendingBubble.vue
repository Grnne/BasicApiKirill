<script setup lang="ts">
import type { PendingMessage } from '@/entities/message/model/history'

defineProps<{ message: PendingMessage }>()
defineEmits<{ retry: []; discard: [] }>()
</script>

<template>
  <article :class="['bubble', message.state]">
    <p v-if="message.attachments.length > 0" class="files">📎 Файлов: {{ message.attachments.length }}</p>
    <p v-if="message.text" class="text">{{ message.text }}</p>
    <span v-if="message.state === 'sending'" class="status">отправляется…</span>
    <span v-else class="status failed" role="alert">
      Не отправлено: {{ message.error }}
      <button type="button" class="action" @click="$emit('retry')">Повторить</button>
      <button type="button" class="action" @click="$emit('discard')">Удалить</button>
    </span>
  </article>
</template>

<style scoped>
.bubble {
  max-width: min(560px, 75%);
  align-self: flex-end;
  padding: 7px 11px;
  border-radius: var(--radius);
  border-bottom-right-radius: 2px;
  background: var(--accent-soft);
}
.bubble.sending {
  opacity: 0.7;
}
.bubble.failed {
  border: 1px solid var(--danger);
}
.files {
  margin: 0;
  color: var(--text-dim);
  font-size: 12px;
}
.text {
  margin: 0;
  overflow-wrap: anywhere;
  white-space: pre-wrap;
}
.status {
  display: block;
  margin-top: 2px;
  color: var(--text-faint);
  font-size: 11px;
  text-align: right;
}
.status.failed {
  color: var(--danger);
}
.action {
  margin-left: 6px;
  padding: 0;
  border: none;
  background: none;
  color: var(--text);
  font-size: 11px;
  text-decoration: underline;
  cursor: pointer;
}
</style>
