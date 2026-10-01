<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'

import { isMutedNow } from '@/entities/chat/lib'
import type { ChatListItem } from '@/entities/chat/types'
import { useChatListStore } from '../model/chat-list.store'

const props = defineProps<{ chat: ChatListItem; x: number; y: number }>()
const emit = defineEmits<{ close: [] }>()

const store = useChatListStore()

const unread = computed(() => props.chat.unreadCount > 0 || props.chat.markedUnread)
const pinned = computed(() => props.chat.pinnedPosition !== null)
const folder = computed(() => store.selectedFolder)
const pinnedInFolder = computed(() => !!folder.value?.pinnedChatIds.includes(props.chat.chatId))
const muted = computed(() => isMutedNow(props.chat))
const choosingMute = ref(false)

const HOUR = 3_600_000
const MUTE_FOR = [
  { label: 'На 1 час', ms: HOUR },
  { label: 'На 8 часов', ms: 8 * HOUR },
  { label: 'На сутки', ms: 24 * HOUR },
  { label: 'Навсегда', ms: null },
] as const

// Kept inside the window: the menu opens at the pointer.
const style = computed(() => ({
  left: `${Math.min(props.x, window.innerWidth - 220)}px`,
  top: `${Math.min(props.y, window.innerHeight - 260)}px`,
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
  <ul v-if="choosingMute" class="menu" role="menu" :style="style" @click.stop>
    <li v-for="option in MUTE_FOR" :key="option.label">
      <button
        type="button"
        role="menuitem"
        @click="run(() => store.mute(chat.chatId, option.ms === null ? null : Date.now() + option.ms))"
      >
        {{ option.label }}
      </button>
    </li>
  </ul>
  <ul v-else class="menu" role="menu" :style="style" @click.stop>
    <li v-if="folder">
      <button
        type="button"
        role="menuitem"
        @click="run(() => store.pinInFolder(folder!, chat.chatId, !pinnedInFolder))"
      >
        {{ pinnedInFolder ? 'Открепить в папке' : 'Закрепить в папке' }}
      </button>
    </li>
    <li v-else>
      <button type="button" role="menuitem" @click="run(() => store.pin(chat.chatId, !pinned))">
        {{ pinned ? 'Открепить' : 'Закрепить' }}
      </button>
    </li>
    <li>
      <button type="button" role="menuitem" @click="run(() => store.archive(chat.chatId, !chat.archived))">
        {{ chat.archived ? 'Вернуть из архива' : 'В архив' }}
      </button>
    </li>
    <li v-if="muted">
      <button type="button" role="menuitem" @click="run(() => store.unmute(chat.chatId))">Включить уведомления</button>
    </li>
    <li v-else>
      <button type="button" role="menuitem" @click="choosingMute = true">Без звука…</button>
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
