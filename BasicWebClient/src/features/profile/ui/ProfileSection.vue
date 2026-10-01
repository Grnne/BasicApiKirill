<script setup lang="ts">
import { computed, ref, watch } from 'vue'

import { useConfigStore } from '@/entities/config/config.store'
import { AVATAR_ACCEPT, rejectAvatar, uploadPhoto } from '@/entities/media/photo'
import AvatarCircle from '@/entities/media/ui/AvatarCircle.vue'
import { useAccountStore } from '@/entities/user/model/account.store'
import { describeError } from '@/shared/api/problem'
import { useNoticesStore } from '@/shared/ui/notices.store'
import * as profileApi from '../api/profile.api'

/** Matches the server: 1–100 characters after trimming. */
const MAX_NAME = 100

const account = useAccountStore()
const config = useConfigStore()
const notices = useNoticesStore()

const me = computed(() => account.me)
const name = ref(me.value?.displayName ?? '')
// The snapshot may come after the page opened; an edit in progress is not overwritten.
watch(
  () => me.value?.displayName,
  (now, before) => {
    if (now !== undefined && name.value === (before ?? '')) name.value = now
  },
)

const trimmed = computed(() => name.value.trim())
const nameChanged = computed(() => !!me.value && trimmed.value !== me.value.displayName)
const nameValid = computed(() => trimmed.value.length > 0 && trimmed.value.length <= MAX_NAME)

const busy = ref(false)
const error = ref<string | null>(null)

async function saveName(): Promise<void> {
  if (busy.value || !nameChanged.value || !nameValid.value) return
  busy.value = true
  error.value = null
  try {
    account.setMe(await profileApi.updateProfile(trimmed.value))
    name.value = account.me?.displayName ?? trimmed.value
    notices.push('Имя сохранено', 'info')
  } catch (e) {
    error.value = describeError(e)
  } finally {
    busy.value = false
  }
}

const photoInput = ref<HTMLInputElement | null>(null)

async function onPhotoChosen(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0] ?? null
  input.value = ''
  if (!file) return
  error.value = rejectAvatar(file, config.config)
  if (error.value) return
  await setPhoto(file)
}

async function setPhoto(file: File | null): Promise<void> {
  busy.value = true
  error.value = null
  try {
    const attachment = file ? await uploadPhoto(file) : null
    account.setMe(await profileApi.setMyAvatar(attachment?.id ?? null))
  } catch (e) {
    error.value = describeError(e)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <section v-if="me" class="section">
    <h2 class="heading">Профиль</h2>

    <div class="top">
      <AvatarCircle :avatar-id="me.avatarId" :initial="me.displayName.charAt(0).toUpperCase() || '?'" :size="72" />
      <div class="photo-actions">
        <button type="button" class="link" :disabled="busy" @click="photoInput?.click()">
          {{ me.avatarId ? 'Сменить фото' : 'Поставить фото' }}
        </button>
        <button v-if="me.avatarId" type="button" class="link danger" :disabled="busy" @click="setPhoto(null)">
          Убрать фото
        </button>
        <input ref="photoInput" type="file" :accept="AVATAR_ACCEPT" hidden @change="onPhotoChosen" />
      </div>
    </div>

    <form class="row" @submit.prevent="saveName">
      <label class="field">
        <span class="label">Имя — его видят другие</span>
        <input v-model="name" class="input" type="text" :maxlength="MAX_NAME" autocomplete="nickname" />
      </label>
      <button type="submit" class="button" :disabled="busy || !nameChanged || !nameValid">Сохранить</button>
    </form>

    <dl class="facts">
      <dt>Логин</dt>
      <dd>@{{ me.username }}</dd>
      <dt>Email</dt>
      <dd>{{ me.email }}</dd>
    </dl>

    <p v-if="error" class="error">{{ error }}</p>
  </section>
</template>

<style scoped src="@/shared/ui/section.css"></style>

<style scoped>
.top {
  display: flex;
  align-items: center;
  gap: 16px;
}
.photo-actions {
  display: grid;
  justify-items: start;
  gap: 4px;
}
.facts {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 4px 12px;
  margin: 0;
}
.facts dt {
  color: var(--text-dim);
  font-size: 12px;
}
.facts dd {
  margin: 0;
}
</style>
