<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'

import { useConfigStore } from '@/entities/config/config.store'
import { messageActions } from '@/entities/message/lib/actions'
import type { Message } from '@/entities/message/types'
import ConfirmDialog from '@/shared/ui/ConfirmDialog.vue'
import { useMessagesStore } from '../model/messages.store'
import MessageBubble from './MessageBubble.vue'

const props = defineProps<{ message: Message; meId: string | null }>()

const store = useMessagesStore()
const config = useConfigStore()

const own = computed(() => props.message.senderId === props.meId)
const system = computed(() => props.message.type === 'system')
const actions = computed(() =>
  messageActions(props.message, {
    meId: props.meId,
    now: Date.now(),
    editWindowHours: config.config.messages.editWindowHours,
    deleteWindowHours: config.config.messages.deleteWindowHours,
  }),
)

const menuOpen = ref(false)
const confirmingDelete = ref(false)
const forEveryone = ref(false)

function close(): void {
  menuOpen.value = false
}

// Any click elsewhere closes the menu.
watch(menuOpen, (open) => {
  if (open) setTimeout(() => document.addEventListener('click', close, { once: true }))
  else document.removeEventListener('click', close)
})
onUnmounted(() => document.removeEventListener('click', close))

function copy(): void {
  void navigator.clipboard?.writeText(props.message.text)
  close()
}

function edit(): void {
  store.startEdit(props.message)
  close()
}

function askDelete(): void {
  forEveryone.value = false
  confirmingDelete.value = true
  close()
}

async function confirmDelete(): Promise<void> {
  confirmingDelete.value = false
  await store.remove(props.message, forEveryone.value && actions.value.deleteForEveryone)
}
</script>

<template>
  <MessageBubble v-if="system" :message="message" :own="false" :me-id="meId" />

  <div v-else :class="['item', { own }]">
    <MessageBubble :message="message" :own="own" :me-id="meId" />
    <div class="tools">
      <button
        type="button"
        class="more"
        title="Действия"
        aria-haspopup="menu"
        :aria-expanded="menuOpen"
        @click.stop="menuOpen = !menuOpen"
      >
        ⋯
      </button>
      <ul v-if="menuOpen" class="menu" role="menu">
        <li v-if="actions.edit"><button type="button" role="menuitem" @click="edit">Изменить</button></li>
        <li v-if="actions.copy"><button type="button" role="menuitem" @click="copy">Копировать текст</button></li>
        <li v-if="actions.deleteForMe">
          <button type="button" role="menuitem" class="danger" @click="askDelete">Удалить</button>
        </li>
      </ul>
    </div>

    <ConfirmDialog
      v-if="confirmingDelete"
      title="Удалить сообщение?"
      confirm-label="Удалить"
      danger
      @confirm="confirmDelete"
      @cancel="confirmingDelete = false"
    >
      <label v-if="actions.deleteForEveryone" class="everyone">
        <input v-model="forEveryone" type="checkbox" />
        Удалить у всех
      </label>
      <p v-else class="note">Сообщение удалится только у вас.</p>
    </ConfirmDialog>
  </div>
</template>

<style scoped>
.item {
  position: relative;
  display: flex;
  align-items: flex-end;
  gap: 4px;
  max-width: 100%;
}
.item.own {
  flex-direction: row-reverse;
  align-self: flex-end;
}
.item :deep(.bubble) {
  max-width: min(560px, 75vw);
}
.tools {
  position: relative;
}
.more {
  padding: 0 6px;
  border: none;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-faint);
  font-size: 16px;
  opacity: 0;
  transition: opacity 0.1s;
}
.item:hover .more,
.more:focus-visible,
.more[aria-expanded='true'] {
  opacity: 1;
}
@media (hover: none) {
  .more {
    opacity: 0.6;
  }
}
.menu {
  position: absolute;
  bottom: 100%;
  left: 0;
  z-index: 20;
  min-width: 170px;
  margin: 0 0 4px;
  padding: 4px;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--surface-solid);
  box-shadow: 0 4px 16px #0008;
  list-style: none;
}
.item.own .menu {
  right: 0;
  left: auto;
}
.menu button {
  display: block;
  width: 100%;
  padding: 6px 10px;
  border: none;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text);
  text-align: left;
}
.menu button:hover {
  background: var(--surface-hover);
}
.menu .danger {
  color: var(--danger);
}
.everyone {
  display: flex;
  align-items: center;
  gap: 8px;
}
.note {
  margin: 0;
  color: var(--text-dim);
}
</style>
