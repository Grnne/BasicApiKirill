<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'

import { describeError } from '@/shared/api/problem'
import * as authApi from '../api/auth.api'

const router = useRouter()

/** Shown only where registration is by invitation. */
const shown = ref(false)
const link = ref<string | null>(null)
const expiresAt = ref<string | null>(null)
const copied = ref(false)
const busy = ref(false)
const error = ref<string | null>(null)

onMounted(async () => {
  try {
    shown.value = (await authApi.getRegistration()).mode === 'invite'
  } catch {
    shown.value = false
  }
})

async function invite(): Promise<void> {
  busy.value = true
  error.value = null
  copied.value = false
  try {
    const created = await authApi.createInvite()
    link.value = window.location.origin + router.resolve({ name: 'login', query: { invite: created.code } }).href
    expiresAt.value = new Date(created.expiresAt).toLocaleDateString('ru-RU')
  } catch (e) {
    error.value = describeError(e)
  } finally {
    busy.value = false
  }
}

async function copy(): Promise<void> {
  if (!link.value) return
  try {
    await navigator.clipboard.writeText(link.value)
    copied.value = true
  } catch {
    // No clipboard here (an insecure page, a refusal): the link is on screen to copy by hand.
  }
}
</script>

<template>
  <section v-if="shown" class="section">
    <h2 class="heading">Пригласить</h2>
    <p class="hint">Регистрация на этом сервере — по приглашению. Ссылка работает один раз.</p>
    <button type="button" class="button" :disabled="busy" @click="invite">
      {{ busy ? 'Создаём…' : 'Создать ссылку-приглашение' }}
    </button>
    <template v-if="link">
      <input class="input" :value="link" readonly aria-label="Ссылка-приглашение" @focus="($event.target as HTMLInputElement).select()" />
      <p class="hint">
        Действует до {{ expiresAt }}.
        <button type="button" class="link" @click="copy">{{ copied ? 'Скопировано' : 'Скопировать' }}</button>
      </p>
    </template>
    <p v-if="error" class="error" role="alert">{{ error }}</p>
  </section>
</template>

<style scoped src="@/shared/ui/section.css"></style>
