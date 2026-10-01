<script setup lang="ts">
import { ref } from 'vue'

import { useNoticesStore } from '@/shared/ui/notices.store'
import { useMessagesStore } from '../model/messages.store'
import ForwardDialog from './ForwardDialog.vue'

const store = useMessagesStore()
const notices = useNoticesStore()
const forwarding = ref(false)

async function forwardTo(chatId: string): Promise<void> {
  forwarding.value = false
  const count = await store.forward(chatId, [...store.selected])
  if (count > 0) notices.push(`Переслано сообщений: ${count}`, 'info')
}
</script>

<template>
  <div class="bar">
    <span class="count">Выбрано: {{ store.selected.size }}</span>
    <button type="button" class="action primary" @click="forwarding = true">Переслать</button>
    <button type="button" class="action" @click="store.clearSelection()">Отмена</button>
    <ForwardDialog
      v-if="forwarding"
      :count="store.selected.size"
      @pick="forwardTo"
      @cancel="forwarding = false"
    />
  </div>
</template>

<style scoped>
.bar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 12px;
  border-top: 1px solid var(--border);
  background: var(--surface-solid);
}
.count {
  margin-right: auto;
  color: var(--text-dim);
}
.action {
  padding: 7px 14px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text);
}
.action.primary {
  border-color: transparent;
  background: var(--accent);
  color: #04160b;
  font-weight: 600;
}
</style>
