<script setup lang="ts">
// Поле формы: подпись + input. Значение через v-model.
const model = defineModel<string>({ required: true })

defineProps<{
  label: string
  type?: string
  /** Подсказка браузеру и менеджеру паролей: username, current-password, new-password, email. */
  autocomplete?: string
  disabled?: boolean
  required?: boolean
}>()
</script>

<template>
  <label class="field">
    <span class="label">{{ label }}</span>
    <input
      v-model="model"
      :type="type ?? 'text'"
      :autocomplete="autocomplete"
      :disabled="disabled"
      :required="required"
      class="input"
    />
  </label>
</template>

<style scoped>
.field {
  display: grid;
  gap: 5px;
}
.label {
  color: var(--text-dim);
  font-size: 12px;
  text-transform: uppercase;
  letter-spacing: 0.6px;
}
.input {
  padding: 9px 11px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface-solid);
  transition: border-color 0.15s;
}
.input:hover:not(:disabled) {
  border-color: var(--accent-dim);
}
.input:focus {
  border-color: var(--accent);
  outline: none;
}
.input:disabled {
  opacity: 0.5;
}
</style>
