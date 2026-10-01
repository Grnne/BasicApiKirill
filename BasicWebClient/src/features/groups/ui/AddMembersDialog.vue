<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'

import * as chatApi from '@/entities/chat/api'
import { useChatDetailsStore } from '@/entities/chat/model/details.store'
import { useConfigStore } from '@/entities/config/config.store'
import type { UserSearchResult } from '@/entities/user/types'
import { describeAddError } from '../lib/refusal'
import UserPicker from './UserPicker.vue'

const props = defineProps<{ chatId: string; meId: string }>()
const emit = defineEmits<{ done: [added: number]; cancel: [] }>()

const config = useConfigStore()
const details = useChatDetailsStore()

const members = computed(() => details.get(props.chatId)?.participants ?? [])
const memberIds = computed(() => new Set(members.value.map((m) => m.userId)))
const room = computed(() => Math.max(0, config.config.groups.maxMembers - members.value.length))

const picked = ref<UserSearchResult[]>([])
const busy = ref(false)
const error = ref<string | null>(null)

async function add(): Promise<void> {
  if (busy.value || picked.value.length === 0) return
  busy.value = true
  error.value = null
  try {
    const added = await chatApi.addMembers(props.chatId, picked.value.map((u) => u.userId))
    // The card shows them at once; MemberAdded brings the same again and changes nothing.
    details.apply('MemberAdded', { chatId: props.chatId, addedBy: props.meId, members: added }, props.meId)
    emit('done', added.length)
  } catch (e) {
    error.value = describeAddError(e, picked.value)
  } finally {
    busy.value = false
  }
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape' && !busy.value) emit('cancel')
}
onMounted(() => document.addEventListener('keydown', onKeydown))
onUnmounted(() => document.removeEventListener('keydown', onKeydown))
</script>

<template>
  <div class="overlay" @click.self="!busy && emit('cancel')">
    <div class="dialog" role="dialog" aria-modal="true" aria-label="Добавить участников">
      <h2 class="heading">Добавить участников</h2>
      <UserPicker v-model="picked" :max="room" :excluded="memberIds" />
      <p v-if="error" class="error">{{ error }}</p>
      <div class="buttons">
        <button type="button" class="button" :disabled="busy" @click="emit('cancel')">Отмена</button>
        <button type="button" class="button primary" :disabled="busy || picked.length === 0" @click="add">
          {{ busy ? 'Добавляем…' : 'Добавить' }}
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
  display: grid;
  gap: 10px;
  width: min(420px, 100%);
  max-height: calc(100vh - 32px);
  padding: 18px;
  overflow: hidden;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--surface-solid);
}
.heading {
  margin: 0;
  font-size: 16px;
}
.error {
  margin: 0;
  color: var(--danger);
  font-size: 12px;
}
.buttons {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
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
.button:disabled {
  opacity: 0.5;
}
</style>
