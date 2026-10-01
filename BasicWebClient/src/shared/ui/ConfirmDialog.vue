<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'

defineProps<{
  title: string
  text?: string
  confirmLabel: string
  danger?: boolean
}>()
const emit = defineEmits<{ confirm: []; cancel: [] }>()

const confirmButton = ref<HTMLButtonElement | null>(null)

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') emit('cancel')
}

onMounted(() => {
  document.addEventListener('keydown', onKeydown)
  confirmButton.value?.focus()
})
onUnmounted(() => document.removeEventListener('keydown', onKeydown))
</script>

<template>
  <div class="overlay" @click.self="emit('cancel')">
    <div class="dialog" role="dialog" aria-modal="true" :aria-label="title">
      <h2 class="title">{{ title }}</h2>
      <p v-if="text" class="text">{{ text }}</p>
      <slot />
      <div class="buttons">
        <button type="button" class="button" @click="emit('cancel')">Отмена</button>
        <button
          ref="confirmButton"
          type="button"
          :class="['button', danger ? 'danger' : 'primary']"
          @click="emit('confirm')"
        >
          {{ confirmLabel }}
        </button>
      </div>
    </div>
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
  width: min(380px, 100%);
  padding: 18px;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--surface-solid);
}
.title {
  margin: 0 0 8px;
  font-size: 16px;
}
.text {
  margin: 0 0 12px;
  color: var(--text-dim);
}
.buttons {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 14px;
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
.button.danger {
  border-color: transparent;
  background: var(--danger);
  color: #fff;
  font-weight: 600;
}
</style>
