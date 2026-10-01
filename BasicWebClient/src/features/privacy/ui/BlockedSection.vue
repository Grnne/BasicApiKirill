<script setup lang="ts">
import { ref, shallowRef, watch } from 'vue'

import AvatarCircle from '@/entities/media/ui/AvatarCircle.vue'
import * as usersApi from '@/entities/user/api'
import { useAccountStore } from '@/entities/user/model/account.store'
import type { UserProfile } from '@/entities/user/types'
import { describeError } from '@/shared/api/problem'

const account = useAccountStore()

const people = shallowRef<UserProfile[]>([])
const loading = ref(true)
const error = ref<string | null>(null)

async function load(): Promise<void> {
  try {
    people.value = await usersApi.getBlocked()
    error.value = null
  } catch (e) {
    error.value = describeError(e)
  } finally {
    loading.value = false
  }
}

// The block list may change on another device or from a chat: the names come from the server.
watch(
  () => account.blocked,
  () => void load(),
  { immediate: true },
)

const busy = ref<string | null>(null)

async function unblock(userId: string): Promise<void> {
  busy.value = userId
  try {
    await usersApi.unblockUser(userId)
    account.apply('BlockListChanged', { userId, blocked: false })
  } catch (e) {
    error.value = describeError(e)
  } finally {
    busy.value = null
  }
}
</script>

<template>
  <section class="section">
    <h2 class="heading">Заблокированные</h2>
    <p class="hint">Заблокированные не могут вам писать, добавлять вас в группы и не видят ваш статус и фото.</p>
    <p v-if="loading" class="hint">загрузка…</p>
    <p v-else-if="people.length === 0 && !error" class="hint">Никого</p>
    <ul v-else class="list">
      <li v-for="p in people" :key="p.userId" class="person">
        <AvatarCircle :avatar-id="p.avatarId" :initial="p.displayName.charAt(0).toUpperCase() || '?'" :size="32" />
        <span class="name">{{ p.displayName }} <span class="username">@{{ p.username }}</span></span>
        <button type="button" class="link" :disabled="busy === p.userId" @click="unblock(p.userId)">Разблокировать</button>
      </li>
    </ul>
    <p v-if="error" class="error">{{ error }}</p>
  </section>
</template>

<style scoped src="@/shared/ui/section.css"></style>

<style scoped>
.list {
  display: grid;
  gap: 6px;
  margin: 0;
  padding: 0;
  list-style: none;
}
.person {
  display: flex;
  align-items: center;
  gap: 10px;
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
</style>
