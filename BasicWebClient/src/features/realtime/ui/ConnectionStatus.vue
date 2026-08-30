<script setup lang="ts">
import { computed } from 'vue'

import { useHubStore } from '@/shared/api/hub.store'

const hub = useHubStore()

const label = computed(() => {
  switch (hub.status) {
    case 'connected':
      return 'на связи'
    case 'connecting':
      return 'подключение'
    case 'reconnecting':
      return 'переподключение'
    default:
      return 'нет связи'
  }
})
</script>

<template>
  <span class="status" :data-state="hub.status" :title="`SignalR: ${hub.status}`">
    <span class="dot" />
    {{ label }}
  </span>
</template>

<style scoped>
.status {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  color: var(--text-dim);
  font-size: 12px;
}
.dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--text-faint);
}
.status[data-state='connected'] .dot {
  background: var(--accent);
  box-shadow: 0 0 6px var(--accent);
}
.status[data-state='connecting'] .dot,
.status[data-state='reconnecting'] .dot {
  background: var(--warning);
  animation: pulse 1s ease-in-out infinite;
}
.status[data-state='disconnected'] .dot {
  background: var(--danger);
}
@keyframes pulse {
  50% {
    opacity: 0.3;
  }
}
</style>
