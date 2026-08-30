<script setup lang="ts">
import { ref, watch } from 'vue'

import type { UserSearchResult } from '@/entities/user/types'
import { useDebounced } from '@/shared/lib/useDebounced'
import * as usersApi from '../api/users.api'

const props = defineProps<{ query: string }>()
const emit = defineEmits<{ select: [userId: string] }>()

const found = ref<UserSearchResult[]>([])
const debouncedQuery = useDebounced(() => props.query)

let inFlight: AbortController | null = null

watch(debouncedQuery, async (query) => {
  inFlight?.abort()

  const trimmed = query.trim()
  if (trimmed.length === 0) {
    found.value = []
    return
  }

  const controller = new AbortController()
  inFlight = controller

  try {
    const response = await usersApi.searchUsers(trimmed, controller.signal)
    found.value = response.items
  } catch {
    if (!controller.signal.aborted) found.value = []
  }
})
</script>

<template>
  <section v-if="found.length > 0" class="panel">
    <h2 class="heading">Пользователи</h2>

    <button
      v-for="user in found"
      :key="user.userId"
      type="button"
      class="row"
      @click="emit('select', user.userId)"
    >
      <span class="avatar">{{ user.displayName.charAt(0).toUpperCase() }}</span>
      <span class="middle">
        <span class="name">{{ user.displayName }}</span>
        <span class="username">@{{ user.username }}</span>
      </span>
    </button>
  </section>
</template>

<style scoped>
.panel {
  display: grid;
  align-content: start;
  border-top: 1px solid var(--border);
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
.row {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  gap: 10px;
  padding: 8px 12px;
  border: none;
  background: transparent;
  color: inherit;
  text-align: left;
}
.row:hover {
  background: var(--surface-hover);
}
.avatar {
  display: grid;
  place-items: center;
  width: 30px;
  height: 30px;
  border-radius: 50%;
  border: 1px dashed var(--border);
  color: var(--text-dim);
  font-weight: 700;
}
.middle {
  display: grid;
  min-width: 0;
}
.name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.username {
  color: var(--text-faint);
  font-size: 12px;
}
</style>
