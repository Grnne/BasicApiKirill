<script setup lang="ts">
defineProps<{
  type?: 'button' | 'submit'
  variant?: 'primary' | 'ghost'
  disabled?: boolean
}>()
</script>

<template>
  <button
    :type="type ?? 'button'"
    :disabled="disabled"
    :class="['btn', variant === 'ghost' ? 'ghost' : 'primary']"
  >
    <slot />
  </button>
</template>

<style scoped>
.btn {
  padding: 9px 16px;
  border: 1px solid transparent;
  border-radius: var(--radius-sm);
  font-weight: 600;
  transition:
    background 0.15s,
    border-color 0.15s;
}
.btn:disabled {
  cursor: default;
  opacity: 0.45;
}
.primary {
  background: var(--accent);
  color: #04160b;
}
.primary:hover:not(:disabled) {
  background: var(--accent-dim);
}
.ghost {
  border-color: var(--border);
  background: transparent;
  color: var(--text-dim);
}
.ghost:hover:not(:disabled) {
  border-color: var(--accent-dim);
  color: var(--text);
}
</style>
