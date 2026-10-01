<script setup lang="ts">
import { ref } from 'vue'

import { useAccountStore } from '@/entities/user/model/account.store'
import { describeError } from '@/shared/api/problem'
import { updatePrivacy, type PrivacyKey, type PrivacyValue } from '../api/privacy.api'

const account = useAccountStore()

const SETTINGS: { key: PrivacyKey; label: string; hint: string }[] = [
  { key: 'lastSeen', label: 'Кто видит, что я в сети', hint: 'Скрыв своё, вы не увидите и чужое' },
  { key: 'messages', label: 'Кто может начать со мной чат', hint: 'Уже начатые чаты продолжают работать' },
  { key: 'groupAdd', label: 'Кто может добавлять меня в группы', hint: '' },
]
const OPTIONS: { value: PrivacyValue; label: string }[] = [
  { value: 'everybody', label: 'Все' },
  { value: 'contacts', label: 'Те, с кем есть общий чат' },
  { value: 'nobody', label: 'Никто' },
]

/** Never changed — everybody. */
const current = (key: PrivacyKey): PrivacyValue => (account.privacy?.[key] as PrivacyValue | null) ?? 'everybody'

const busy = ref<PrivacyKey | null>(null)
const error = ref<string | null>(null)

async function change(key: PrivacyKey, value: PrivacyValue): Promise<void> {
  busy.value = key
  error.value = null
  try {
    account.setPrivacy(await updatePrivacy({ [key]: value }))
  } catch (e) {
    error.value = describeError(e)
  } finally {
    busy.value = null
  }
}
</script>

<template>
  <section class="section">
    <h2 class="heading">Приватность</h2>
    <label v-for="s in SETTINGS" :key="s.key" class="field">
      <span class="label">{{ s.label }}</span>
      <select
        class="input"
        :value="current(s.key)"
        :disabled="busy !== null"
        @change="change(s.key, ($event.target as HTMLSelectElement).value as PrivacyValue)"
      >
        <option v-for="o in OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</option>
      </select>
      <span v-if="s.hint" class="hint">{{ s.hint }}</span>
    </label>
    <p v-if="error" class="error">{{ error }}</p>
  </section>
</template>

<style scoped src="@/shared/ui/section.css"></style>
