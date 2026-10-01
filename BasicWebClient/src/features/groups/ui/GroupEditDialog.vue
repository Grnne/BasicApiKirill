<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref } from 'vue'

import * as chatApi from '@/entities/chat/api'
import { MEMBER_PERMISSIONS, PERMISSION_LABELS, canDeleteGroup, canEditDefaults, canEditInfo } from '@/entities/chat/members'
import { useChatsStore } from '@/entities/chat/model/chats.store'
import { useChatDetailsStore } from '@/entities/chat/model/details.store'
import { useConfigStore } from '@/entities/config/config.store'
import { AVATAR_ACCEPT, rejectAvatar, uploadPhoto } from '@/entities/media/photo'
import AvatarCircle from '@/entities/media/ui/AvatarCircle.vue'
import { describeError } from '@/shared/api/problem'
import type { ChatUpdatedDto, PermissionsPatchDto } from '@/shared/api/schema'
import ConfirmDialog from '@/shared/ui/ConfirmDialog.vue'

const props = defineProps<{ chatId: string; meId: string }>()
/** deleting — before the request: ChatDeleted may come before its answer. */
const emit = defineEmits<{ done: []; deleting: []; deleted: []; cancel: [] }>()

const config = useConfigStore()
const chats = useChatsStore()
const details = useChatDetailsStore()

const detail = computed(() => details.get(props.chatId))
const mayInfo = computed(() => !!detail.value && canEditInfo(detail.value))
const mayDefaults = computed(() => !!detail.value && canEditDefaults(detail.value))
const mayDelete = computed(() => !!detail.value && canDeleteGroup(detail.value))

const title = ref(detail.value?.title ?? '')
const defaults = reactive<PermissionsPatchDto>(
  Object.fromEntries(MEMBER_PERMISSIONS.map((p) => [p, detail.value?.memberPermissions?.[p] ?? false])),
)

/* ── Photo: undefined — unchanged, null — removed, a File — new ── */

const photo = ref<File | null | undefined>(undefined)
const preview = ref<string | null>(null)
const photoError = ref<string | null>(null)
const photoInput = ref<HTMLInputElement | null>(null)

function setPreview(file: File | null): void {
  if (preview.value) URL.revokeObjectURL(preview.value)
  preview.value = file ? URL.createObjectURL(file) : null
}

function onPhotoChosen(event: Event): void {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0] ?? null
  input.value = ''
  if (!file) return
  photoError.value = rejectAvatar(file, config.config)
  if (photoError.value) return
  photo.value = file
  setPreview(file)
}

function removePhoto(): void {
  photo.value = null
  setPreview(null)
}

const shownAvatarId = computed(() => (photo.value === undefined ? detail.value?.avatarId ?? null : null))

/* ── Save ── */

const busy = ref(false)
const error = ref<string | null>(null)

const trimmed = computed(() => title.value.trim())
const titleChanged = computed(() => mayInfo.value && trimmed.value !== (detail.value?.title ?? ''))
const changedDefaults = computed<PermissionsPatchDto>(() => {
  if (!mayDefaults.value) return {}
  const current = detail.value?.memberPermissions
  return Object.fromEntries(MEMBER_PERMISSIONS.filter((p) => defaults[p] !== current?.[p]).map((p) => [p, defaults[p]]))
})
const titleValid = computed(() => trimmed.value.length > 0 && trimmed.value.length <= config.config.groups.maxTitleLength)
const canSave = computed(
  () =>
    !busy.value &&
    titleValid.value &&
    (titleChanged.value || Object.keys(changedDefaults.value).length > 0 || photo.value !== undefined),
)

function applyUpdate(update: ChatUpdatedDto): void {
  chats.apply('ChatUpdated', update, { meId: props.meId })
  details.apply('ChatUpdated', update, props.meId)
}

async function save(): Promise<void> {
  if (!canSave.value) return
  busy.value = true
  error.value = null
  try {
    const body: { title?: string; memberPermissions?: PermissionsPatchDto } = {}
    if (titleChanged.value) body.title = trimmed.value
    if (Object.keys(changedDefaults.value).length > 0) body.memberPermissions = changedDefaults.value
    if (body.title !== undefined || body.memberPermissions) applyUpdate(await chatApi.updateGroup(props.chatId, body))

    if (photo.value !== undefined) {
      const attachment = photo.value ? await uploadPhoto(photo.value) : null
      await chatApi.setChatAvatar(props.chatId, attachment?.id ?? null)
    }
    emit('done')
  } catch (e) {
    error.value = describeError(e)
  } finally {
    busy.value = false
  }
}

/* ── Delete ── */

const confirmingDelete = ref(false)

async function deleteGroup(): Promise<void> {
  confirmingDelete.value = false
  busy.value = true
  emit('deleting')
  try {
    await chatApi.deleteGroup(props.chatId)
    emit('deleted')
  } catch (e) {
    error.value = describeError(e)
  } finally {
    busy.value = false
  }
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape' && !busy.value && !confirmingDelete.value) emit('cancel')
}
onMounted(() => document.addEventListener('keydown', onKeydown))
onUnmounted(() => {
  document.removeEventListener('keydown', onKeydown)
  setPreview(null)
})
</script>

<template>
  <div class="overlay" @click.self="!busy && emit('cancel')">
    <form class="dialog" role="dialog" aria-modal="true" aria-label="Настройки группы" @submit.prevent="save">
      <h2 class="heading">Настройки группы</h2>

      <div class="top">
        <button type="button" class="photo" title="Сменить фото" :disabled="!mayInfo" @click="photoInput?.click()">
          <img v-if="preview" :src="preview" alt="" />
          <AvatarCircle v-else :avatar-id="shownAvatarId" :initial="trimmed.charAt(0).toUpperCase() || '?'" :size="56" />
        </button>
        <input ref="photoInput" type="file" :accept="AVATAR_ACCEPT" hidden @change="onPhotoChosen" />
        <label class="field">
          <span class="label">Название</span>
          <input
            v-model="title"
            class="input"
            type="text"
            :maxlength="config.config.groups.maxTitleLength"
            :disabled="!mayInfo"
            autocomplete="off"
          />
        </label>
      </div>
      <p v-if="photoError" class="error">{{ photoError }}</p>
      <button v-if="mayInfo && (shownAvatarId || photo)" type="button" class="link" @click="removePhoto">Убрать фото</button>

      <fieldset v-if="mayDefaults" class="defaults">
        <legend class="label">Участники по умолчанию могут</legend>
        <label v-for="p in MEMBER_PERMISSIONS" :key="p" class="check">
          <input v-model="defaults[p]" type="checkbox" />
          {{ PERMISSION_LABELS[p] }}
        </label>
      </fieldset>

      <p v-if="error" class="error">{{ error }}</p>
      <div class="buttons">
        <button v-if="mayDelete" type="button" class="button danger-link" :disabled="busy" @click="confirmingDelete = true">
          Удалить группу
        </button>
        <button type="button" class="button" :disabled="busy" @click="emit('cancel')">Отмена</button>
        <button type="submit" class="button primary" :disabled="!canSave">{{ busy ? 'Сохраняем…' : 'Сохранить' }}</button>
      </div>
    </form>

    <ConfirmDialog
      v-if="confirmingDelete"
      title="Удалить группу?"
      text="Группа и вся её история пропадут у всех участников. Это не отменить."
      confirm-label="Удалить"
      danger
      @confirm="deleteGroup"
      @cancel="confirmingDelete = false"
    />
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
  padding: 18px;
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
  border: none;
  border-radius: 50%;
  background: var(--surface-hover);
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
.defaults {
  display: grid;
  gap: 4px;
  margin: 0;
  padding: 8px 10px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
}
.check {
  display: flex;
  align-items: center;
  gap: 8px;
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
.button.danger-link {
  margin-right: auto;
  border-color: transparent;
  color: var(--danger);
}
.button:disabled {
  opacity: 0.5;
}
</style>
