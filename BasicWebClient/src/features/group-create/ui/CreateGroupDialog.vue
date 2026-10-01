<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'

import * as chatApi from '@/entities/chat/api'
import { useChatsStore } from '@/entities/chat/model/chats.store'
import { useConfigStore } from '@/entities/config/config.store'
import * as mediaApi from '@/entities/media/api'
import { uploadKind } from '@/entities/media/lib'
import { measureVideo, putFile } from '@/entities/media/transfer'
import { uploadFile } from '@/entities/media/upload'
import AvatarCircle from '@/entities/media/ui/AvatarCircle.vue'
import * as usersApi from '@/entities/user/api'
import type { UserSearchResult } from '@/entities/user/types'
import { describeError } from '@/shared/api/problem'
import { useDebounced } from '@/shared/lib/useDebounced'
import { useNoticesStore } from '@/shared/ui/notices.store'
import { createGroupWithPhoto } from '../model/create-group'

const emit = defineEmits<{ created: [chatId: string]; cancel: [] }>()

const config = useConfigStore()
const chats = useChatsStore()
const notices = useNoticesStore()

const title = ref('')
const maxTitle = computed(() => config.config.groups.maxTitleLength)
/** The owner is a member too. */
const maxPicked = computed(() => config.config.groups.maxMembers - 1)

/* ── Photo ── */

const photo = ref<File | null>(null)
const photoPreview = ref<string | null>(null)
const photoError = ref<string | null>(null)
const photoInput = ref<HTMLInputElement | null>(null)

function setPhoto(file: File | null): void {
  if (photoPreview.value) URL.revokeObjectURL(photoPreview.value)
  photo.value = file
  photoPreview.value = file ? URL.createObjectURL(file) : null
}

function onPhotoChosen(event: Event): void {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0] ?? null
  input.value = ''
  if (!file) return
  // Only what the server decodes as a photo may become an avatar.
  if (uploadKind(file, config.config) !== 'photo') {
    photoError.value = 'Нужна фотография: JPEG, PNG, GIF или WebP'
    return
  }
  photoError.value = null
  setPhoto(file)
}

/* ── Members ── */

const query = ref('')
const debounced = useDebounced(() => query.value.trim())
const found = ref<UserSearchResult[]>([])
const picked = ref<UserSearchResult[]>([])
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

const pickedIds = computed(() => new Set(picked.value.map((u) => u.userId)))

function toggle(user: UserSearchResult): void {
  if (pickedIds.value.has(user.userId)) {
    picked.value = picked.value.filter((u) => u.userId !== user.userId)
  } else if (picked.value.length < maxPicked.value) {
    picked.value = [...picked.value, user]
  }
}

/* ── Create ── */

const busy = ref(false)
const error = ref<string | null>(null)
const canCreate = computed(() => {
  const length = title.value.trim().length
  return !busy.value && length > 0 && length <= maxTitle.value
})

const uploadDeps = { createUpload: mediaApi.createUpload, completeUpload: mediaApi.completeUpload, putFile, measureVideo }

async function create(): Promise<void> {
  if (!canCreate.value) return
  busy.value = true
  error.value = null
  try {
    const result = await createGroupWithPhoto(
      { title: title.value, memberIds: picked.value.map((u) => u.userId), photo: photo.value },
      {
        createGroup: chatApi.createGroup,
        uploadPhoto: (file) => uploadFile(file, 'photo', uploadDeps, () => {}, new AbortController().signal),
        setChatAvatar: chatApi.setChatAvatar,
      },
    )
    chats.put(result.chat)
    if (result.avatarError) notices.push(`Группа создана, но фото не поставилось: ${result.avatarError}`)
    emit('created', result.chat.chatId)
  } catch (e) {
    error.value = describeError(e)
  } finally {
    busy.value = false
  }
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape' && !busy.value) emit('cancel')
}
const titleInput = ref<HTMLInputElement | null>(null)
onMounted(() => {
  document.addEventListener('keydown', onKeydown)
  titleInput.value?.focus()
})
onUnmounted(() => {
  document.removeEventListener('keydown', onKeydown)
  controller?.abort()
  setPhoto(null)
})
</script>

<template>
  <div class="overlay" @click.self="!busy && emit('cancel')">
    <form class="dialog" role="dialog" aria-modal="true" aria-label="Новая группа" @submit.prevent="create">
      <h2 class="heading">Новая группа</h2>

      <div class="top">
        <button type="button" class="photo" title="Фото группы" @click="photoInput?.click()">
          <img v-if="photoPreview" :src="photoPreview" alt="" />
          <span v-else>📷</span>
        </button>
        <input ref="photoInput" type="file" accept="image/jpeg,image/png,image/gif,image/webp" hidden @change="onPhotoChosen" />
        <label class="field">
          <span class="label">Название</span>
          <input ref="titleInput" v-model="title" class="input" type="text" :maxlength="maxTitle" autocomplete="off" />
        </label>
      </div>
      <p v-if="photoError" class="error">{{ photoError }}</p>
      <button v-if="photo" type="button" class="link" @click="setPhoto(null)">Убрать фото</button>

      <div v-if="picked.length > 0" class="chips">
        <button v-for="user in picked" :key="user.userId" type="button" class="chip" title="Убрать" @click="toggle(user)">
          {{ user.displayName }} ✕
        </button>
      </div>

      <input v-model="query" class="input" type="search" placeholder="Добавить участников: имя или логин" autocomplete="off" />
      <p v-if="picked.length >= maxPicked" class="hint">В группе может быть не больше {{ maxPicked + 1 }} участников</p>
      <div class="found">
        <button
          v-for="user in found"
          :key="user.userId"
          type="button"
          :class="['user', { on: pickedIds.has(user.userId) }]"
          @click="toggle(user)"
        >
          <AvatarCircle :avatar-id="user.avatarId" :initial="user.displayName.charAt(0).toUpperCase() || '?'" :size="28" />
          <span class="name">{{ user.displayName }} <span class="username">@{{ user.username }}</span></span>
          <span class="mark">{{ pickedIds.has(user.userId) ? '✓' : '' }}</span>
        </button>
      </div>

      <p v-if="error" class="error">{{ error }}</p>
      <div class="buttons">
        <button type="button" class="button" :disabled="busy" @click="emit('cancel')">Отмена</button>
        <button type="submit" class="button primary" :disabled="!canCreate">
          {{ busy ? 'Создаём…' : 'Создать' }}
        </button>
      </div>
    </form>
  </div>
</template>

<style scoped>
.overlay {
  position: fixed;
  inset: 0;
  z-index: 90;
  display: grid;
  place-items: center;
  padding: 16px;
  background: #000a;
}
.dialog {
  display: grid;
  gap: 10px;
  width: min(420px, 100%);
  max-height: calc(100vh - 32px);
  padding: 18px;
  overflow: hidden;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--surface-solid);
}
.heading {
  margin: 0;
  font-size: 16px;
}
.top {
  display: flex;
  align-items: flex-end;
  gap: 12px;
}
.photo {
  display: grid;
  flex-shrink: 0;
  place-items: center;
  width: 56px;
  height: 56px;
  overflow: hidden;
  padding: 0;
  border: 1px dashed var(--border);
  border-radius: 50%;
  background: var(--surface-hover);
  font-size: 20px;
}
.photo img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}
.field {
  display: grid;
  flex: 1;
  gap: 4px;
}
.label {
  color: var(--text-dim);
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
.found {
  display: grid;
  min-height: 0;
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
.user:hover,
.user.on {
  background: var(--surface-hover);
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
}
.hint {
  margin: 0;
  color: var(--text-dim);
  font-size: 12px;
}
.error {
  margin: 0;
  color: var(--danger);
  font-size: 12px;
}
.link {
  justify-self: start;
  padding: 0;
  border: none;
  background: none;
  color: var(--accent);
  font-size: 12px;
}
.buttons {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
.button {
  padding: 7px 14px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text);
}
.button.primary {
  border-color: transparent;
  background: var(--accent);
  color: #04160b;
  font-weight: 600;
}
.button:disabled {
  opacity: 0.5;
}
</style>
