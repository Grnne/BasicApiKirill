<script setup lang="ts">
import { useRoute, useRouter } from 'vue-router'

import AuthForm from '@/features/auth/ui/AuthForm.vue'

const route = useRoute()
const router = useRouter()

/**
 * The redirect comes from the URL, so only same-origin paths pass: "//evil.com" and
 * "https://evil.com" are absolute URLs and would make an open redirect.
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
