<script setup lang="ts">
import { useRoute, useRouter } from 'vue-router'

import AuthForm from '@/features/auth/ui/AuthForm.vue'

const route = useRoute()
const router = useRouter()

/**
 * Куда вернуться после входа. Значение приходит из адресной строки, поэтому
 * пускаем только внутренние пути: "//evil.com" и "https://evil.com" браузер
 * считает абсолютными адресами — так делают open redirect.
 */
function safeRedirect(): string {
  const target = route.query.redirect
  if (typeof target !== 'string') return '/chat'
  if (!target.startsWith('/') || target.startsWith('//')) return '/chat'
  return target
}

function onSuccess(): void {
  void router.replace(safeRedirect())
}
</script>

<template>
  <main class="page">
    <AuthForm @success="onSuccess" />
  </main>
</template>

<style scoped>
.page {
  display: grid;
  place-items: center;
  height: 100%;
  padding: 20px;
}
</style>
