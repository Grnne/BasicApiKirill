<script setup lang="ts">
import { useNoticesStore } from './notices.store'

const notices = useNoticesStore()
</script>

<template>
  <div class="notices" role="status" aria-live="polite">
    <button
      v-for="notice in notices.items"
      :key="notice.id"
      type="button"
      :class="['notice', notice.kind]"
      title="Закрыть"
      @click="notices.dismiss(notice.id)"
    >
      {{ notice.text }}
    </button>
  </div>
</template>

<style scoped>
.notices {
  position: fixed;
  right: 16px;
  bottom: 16px;
  z-index: 100;
  display: grid;
  gap: 8px;
  max-width: min(360px, calc(100vw - 32px));
}
.notice {
  padding: 10px 14px;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--surface-solid);
  color: var(--text);
  text-align: left;
  box-shadow: 0 4px 16px #0008;
}
.notice.error {
  border-color: var(--danger);
}
</style>
