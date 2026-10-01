<script setup lang="ts">
/* Developer tool, not part of the UI: shown only in dev builds. */
import { onUnmounted, ref } from 'vue'

import { HUB_EVENT_NAMES } from '@/shared/api/hub.types'
import { useHubStore } from '@/shared/api/hub.store'

const MAX_ENTRIES = 50

interface LogEntry {
  id: number
  time: string
  event: string
  payload: string
}

const hub = useHubStore()
const entries = ref<LogEntry[]>([])
let nextId = 0

function add(event: string, args: unknown[]): void {
  entries.value.unshift({
    id: nextId++,
    time: new Date().toLocaleTimeString(),
    event,
    payload: JSON.stringify(args.length === 1 ? args[0] : args),
  })
  if (entries.value.length > MAX_ENTRIES) entries.value.length = MAX_ENTRIES
}

const unsubscribers = HUB_EVENT_NAMES.map((event) =>
  // Each event has its own signature; the log only needs the raw arguments.
  hub.on(event, ((...args: unknown[]) => add(event, args)) as never),
)

onUnmounted(() => {
  for (const unsubscribe of unsubscribers) unsubscribe()
})
</script>

<template>
  <aside class="log">
    <header class="head">события хаба</header>
    <p v-if="entries.length === 0" class="empty">пока тихо</p>
    <ul v-else class="list">
      <li v-for="entry in entries" :key="entry.id" class="item">
        <span class="time">{{ entry.time }}</span>
        <span class="name">{{ entry.event }}</span>
        <span class="payload">{{ entry.payload }}</span>
      </li>
    </ul>
  </aside>
</template>

<style scoped>
.log {
  display: grid;
  grid-template-rows: auto 1fr;
  overflow: hidden;
  border-left: 1px solid var(--border);
  background: var(--surface-solid);
  font-family: var(--font-mono);
  font-size: 11px;
}
.head {
  padding: 8px 10px;
  border-bottom: 1px solid var(--border);
  color: var(--accent);
  letter-spacing: 0.5px;
}
.empty {
  margin: 0;
  padding: 10px;
  color: var(--text-faint);
}
.list {
  margin: 0;
  padding: 6px;
  overflow-y: auto;
  list-style: none;
}
.item {
  display: grid;
  gap: 1px;
  padding: 5px 4px;
  border-bottom: 1px solid var(--border);
}
.time {
  color: var(--text-faint);
}
.name {
  color: var(--accent);
}
.payload {
  overflow-wrap: anywhere;
  color: var(--text-dim);
}
</style>
