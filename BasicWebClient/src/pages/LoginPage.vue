<script setup lang="ts">
import { onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { useAuthStore } from '@/features/auth/model/auth.store'
import AuthForm from '@/features/auth/ui/AuthForm.vue'
import { safeRedirect } from './lib/redirect'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

function onSuccess(): void {
  void router.replace(safeRedirect(route.query.redirect))
}

// The server could not be reached when the page loaded: the sign-in is still there, so it is
// resumed as soon as the server answers instead of asking for the password.
const RESUME_DELAYS_MS = [5_000, 10_000, 30_000]
let resumeTimer: ReturnType<typeof setTimeout> | undefined
let attempt = 0

function scheduleResume(): void {
  if (!auth.canResume) return
  const delay = RESUME_DELAYS_MS[Math.min(attempt, RESUME_DELAYS_MS.length - 1)]
  attempt += 1
  resumeTimer = setTimeout(() => void resume(), delay)
}

async function resume(): Promise<void> {
  clearTimeout(resumeTimer)
  if (!auth.canResume) return
  if (await auth.refreshTokens()) onSuccess()
  else scheduleResume()
}

function onOnline(): void {
  void resume()
}

onMounted(() => {
  scheduleResume()
  window.addEventListener('online', onOnline)
})
onUnmounted(() => {
  clearTimeout(resumeTimer)
  window.removeEventListener('online', onOnline)
})
</script>

<template>
  <main class="page">
    <p v-if="auth.canResume" class="offline" role="status">Нет связи с сервером — войдём, как только он ответит</p>
    <AuthForm @success="onSuccess" />
  </main>
</template>

<style scoped>
.page {
  display: grid;
  place-items: center;
  align-content: center;
  gap: 12px;
  height: 100%;
  padding: 20px;
}
.offline {
  margin: 0;
  color: var(--text-dim);
  text-align: center;
}
</style>
