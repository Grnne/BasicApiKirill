<script setup lang="ts">
import { computed, ref } from 'vue'

import { describeError } from '@/shared/api/problem'
import { useNoticesStore } from '@/shared/ui/notices.store'
import * as profileApi from '../api/profile.api'

/** The server's minimum. */
const MIN_PASSWORD = 6

/** The other devices were signed out: what lists them is out of date. */
const emit = defineEmits<{ changed: [] }>()

const notices = useNoticesStore()

const current = ref('')
const next = ref('')
const repeat = ref('')
const busy = ref(false)
const error = ref<string | null>(null)

const problem = computed(() => {
  if (next.value.length > 0 && next.value.length < MIN_PASSWORD) return `Новый пароль — не короче ${MIN_PASSWORD} символов`
  if (repeat.value.length > 0 && repeat.value !== next.value) return 'Пароли не совпадают'
  return null
})
const ready = computed(
  () => !busy.value && current.value.length > 0 && next.value.length >= MIN_PASSWORD && repeat.value === next.value,
)

async function change(): Promise<void> {
  if (!ready.value) return
  busy.value = true
  error.value = null
  try {
    await profileApi.changePassword(current.value, next.value)
    current.value = next.value = repeat.value = ''
    notices.push('Пароль изменён. На других устройствах нужно войти заново', 'info')
    emit('changed')
  } catch (e) {
    error.value = describeError(e)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <section class="section">
    <h2 class="heading">Пароль</h2>
    <form class="fields" @submit.prevent="change">
      <label class="field">
        <span class="label">Текущий пароль</span>
        <input v-model="current" class="input" type="password" autocomplete="current-password" />
      </label>
      <label class="field">
        <span class="label">Новый пароль</span>
        <input v-model="next" class="input" type="password" autocomplete="new-password" />
      </label>
      <label class="field">
        <span class="label">Новый пароль ещё раз</span>
        <input v-model="repeat" class="input" type="password" autocomplete="new-password" />
      </label>
      <p class="hint">Остальные устройства выйдут сразу, это устройство останется в аккаунте.</p>
      <p v-if="problem || error" class="error">{{ problem ?? error }}</p>
      <button type="submit" class="button" :disabled="!ready">{{ busy ? 'Меняем…' : 'Сменить пароль' }}</button>
    </form>
  </section>
</template>

<style scoped src="@/shared/ui/section.css"></style>

<style scoped>
.fields {
  display: grid;
  gap: 10px;
  max-width: 360px;
}
.fields .button {
  justify-self: start;
}
</style>
