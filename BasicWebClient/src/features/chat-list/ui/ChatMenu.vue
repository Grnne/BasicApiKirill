<script setup lang="ts">
import { computed, onMounted, onUnmounted } from 'vue'

import type { ChatListItem } from '@/entities/chat/types'
import { useChatListStore } from '../model/chat-list.store'

const props = defineProps<{ chat: ChatListItem; x: number; y: number }>()
const emit = defineEmits<{ close: [] }>()

const store = useChatListStore()

const unread = computed(() => props.chat.unreadCount > 0 || props.chat.markedUnread)
const pinned = computed(() => props.chat.pinnedPosition !== null)

// Kept inside the window: the menu opens at the pointer.
const style = computed(() => ({
  left: `${Math.min(props.x, window.innerWidth - 220)}px`,
  top: `${Math.min(props.y, window.innerHeight - 200)}px`,
}))

async function run(action: () => Promise<void>): Promise<void> {
  emit('close')
  await action()
}

function onKey(event: KeyboardEvent): void {
  if (event.key === 'Escape') emit('close')
}
function onOutside(): void {
  emit('close')
}
onMounted(() => {
  document.addEventListener('keydown', onKey)
  setTimeout(() => document.addEventListener('click', onOutside))
})
onUnmounted(() => {
  document.removeEventListener('keydown', onKey)
  document.removeEventListener('click', onOutside)
})
</script>

<template>
  <ul class="menu" role="menu" :style="style" @click.stop>
    <li>
      <button type="button" role="menuitem" @click="run(() => store.pin(chat.chatId, !pinned))">
        {{ pinned ? 'Открепить' : 'Закрепить' }}
      </button>
    </li>
    <li v-if="unread">
      <button type="button" role="menuitem" @click="run(() => store.markRead(chat.chatId))">
        Пометить прочитанным
      </button>
    </li>
    <li v-else>
      <button type="button" role="menuitem" @click="run(() => store.markUnread(chat.chatId))">
        Пометить непрочитанным
      </button>
    </li>
  </ul>
</template>

<style scoped>
.menu {
  position: fixed;
  z-index: 50;
  min-width: 200px;
  margin: 0;
  padding: 4px;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--surface-solid);
  box-shadow: 0 4px 16px #0008;
  list-style: none;
}
.menu button {
  display: block;
  width: 100%;
  padding: 7px 10px;
  border: none;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text);
  text-align: left;
}
.menu button:hover {
  background: var(--surface-hover);
}
</style>
