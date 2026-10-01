<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'

import AvatarCircle from '@/entities/media/ui/AvatarCircle.vue'
import * as usersApi from '@/entities/user/api'
import type { UserSearchResult } from '@/entities/user/types'
import { useDebounced } from '@/shared/lib/useDebounced'

/** People found by name or login, picked into a list. `excluded` — already in the group. */
const props = withDefaults(defineProps<{ max: number; excluded?: ReadonlySet<string> }>(), {
  excluded: () => new Set<string>(),
})
const picked = defineModel<UserSearchResult[]>({ required: true })

const query = ref('')
const debounced = useDebounced(() => query.value.trim())
const found = ref<UserSearchResult[]>([])
let controller: AbortController | null = null

watch(debounced, async (q) => {
  controller?.abort()
  if (q.length < 2) {
    found.value = []
    return
  }
  const current = (controller = new AbortController())
  try {
    const response = await usersApi.searchUsers(q, current.signal)
    if (!current.signal.aborted) found.value = response.items
  } catch {
    if (!current.signal.aborted) found.value = []
  }
})
onUnmounted(() => controller?.abort())

const pickedIds = computed(() => new Set(picked.value.map((u) => u.userId)))

function toggle(user: UserSearchResult): void {
  if (props.excluded.has(user.userId)) return
  if (pickedIds.value.has(user.userId)) {
    picked.value = picked.value.filter((u) => u.userId !== user.userId)
  } else if (picked.value.length < props.max) {
    picked.value = [...picked.value, user]
  }
}
</script>

<template>
  <div class="picker">
    <div v-if="picked.length > 0" class="chips">
      <button v-for="user in picked" :key="user.userId" type="button" class="chip" title="Убрать" @click="toggle(user)">
        {{ user.displayName }} ✕
      </button>
    </div>

    <input v-model="query" class="input" type="search" placeholder="Имя или логин" autocomplete="off" />
    <p v-if="picked.length >= max" class="hint">Больше добавить нельзя: в группе есть предел участников</p>

    <div class="found">
      <button
        v-for="user in found"
        :key="user.userId"
        type="button"
        :class="['user', { on: pickedIds.has(user.userId) }]"
        :disabled="excluded.has(user.userId)"
        @click="toggle(user)"
      >
        <AvatarCircle :avatar-id="user.avatarId" :initial="user.displayName.charAt(0).toUpperCase() || '?'" :size="28" />
        <span class="name">{{ user.displayName }} <span class="username">@{{ user.username }}</span></span>
        <span class="mark">{{ excluded.has(user.userId) ? 'уже в группе' : pickedIds.has(user.userId) ? '✓' : '' }}</span>
      </button>
    </div>
  </div>
</template>

<style scoped>
.picker {
  display: grid;
  gap: 8px;
  min-height: 0;
}
.chips {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}
.chip {
  padding: 2px 8px;
  border: none;
  border-radius: 10px;
  background: var(--accent-soft);
  color: var(--text);
  font-size: 12px;
}
.input {
  width: 100%;
  padding: 7px 10px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface-solid);
}
.input:focus {
  border-color: var(--accent);
  outline: none;
}
.hint {
  margin: 0;
  color: var(--text-dim);
  font-size: 12px;
}
.found {
  display: grid;
  max-height: 240px;
  overflow-y: auto;
}
.user {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 4px;
  border: none;
  background: transparent;
  color: inherit;
  text-align: left;
}
.user:hover:not(:disabled),
.user.on {
  background: var(--surface-hover);
}
.user:disabled {
  opacity: 0.6;
}
.name {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.username {
  color: var(--text-faint);
  font-size: 12px;
}
.mark {
  color: var(--accent);
  font-size: 12px;
}
</style>
