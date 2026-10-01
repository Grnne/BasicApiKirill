<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'

import * as chatApi from '@/entities/chat/api'
import { useChatsStore } from '@/entities/chat/model/chats.store'
import { useConfigStore } from '@/entities/config/config.store'
import { AVATAR_ACCEPT, rejectAvatar, uploadPhoto } from '@/entities/media/photo'
import type { UserSearchResult } from '@/entities/user/types'
import { describeError } from '@/shared/api/problem'
import { useNoticesStore } from '@/shared/ui/notices.store'
import { createGroupWithPhoto } from '../model/create-group'
import UserPicker from './UserPicker.vue'

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
  photoError.value = rejectAvatar(file, config.config)
  if (!photoError.value) setPhoto(file)
}

/* ── Members ── */

const picked = ref<UserSearchResult[]>([])

/* ── Create ── */

const busy = ref(false)
const error = ref<string | null>(null)
const canCreate = computed(() => {
  const length = title.value.trim().length
  return !busy.value && length > 0 && length <= maxTitle.value
})

async function create(): Promise<void> {
  if (!canCreate.value) return
  busy.value = true
  error.value = null
  try {
    const result = await createGroupWithPhoto(
      { title: title.value, memberIds: picked.value.map((u) => u.userId), photo: photo.value },
      {
        createGroup: chatApi.createGroup,
        uploadPhoto: (file) => uploadPhoto(file),
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
        <input ref="photoInput" type="file" :accept="AVATAR_ACCEPT" hidden @change="onPhotoChosen" />
        <label class="field">
          <span class="label">Название</span>
          <input ref="titleInput" v-model="title" class="input" type="text" :maxlength="maxTitle" autocomplete="off" />
        </label>
      </div>
      <p v-if="photoError" class="error">{{ photoError }}</p>
      <button v-if="photo" type="button" class="link" @click="setPhoto(null)">Убрать фото</button>

      <span class="label">Участники</span>
      <UserPicker v-model="picked" :max="maxPicked" />

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
