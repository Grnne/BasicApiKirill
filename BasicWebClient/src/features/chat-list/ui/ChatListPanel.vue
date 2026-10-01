<script setup lang="ts">
import { computed, ref, watch } from 'vue'

import type { ChatListItem } from '@/entities/chat/types'
import { useChatsStore } from '@/entities/chat/model/chats.store'
import { useDebounced } from '@/shared/lib/useDebounced'
import * as chatsApi from '../api/chats.api'
import { useChatListStore } from '../model/chat-list.store'
import ChatMenu from './ChatMenu.vue'
import ChatRow from './ChatRow.vue'

const props = defineProps<{ query: string }>()

const store = useChatListStore()
const chats = useChatsStore()

const showArchive = ref(false)
const visible = computed(() => (showArchive.value ? chats.archivedList : chats.mainList))
const archiveUnread = computed(() => chats.archivedList.reduce((n, c) => n + c.unreadCount, 0))

/** The chat whose menu is open and where (right click or long press on a row). */
const menu = ref<{ chat: ChatListItem; x: number; y: number } | null>(null)

function openMenu(chat: ChatListItem, event: MouseEvent): void {
  menu.value = { chat, x: event.clientX, y: event.clientY }
}

/* Pinned chats are reordered by dragging one onto another. */
const dragged = ref<string | null>(null)

function onDragStart(chat: ChatListItem, event: DragEvent): void {
  if (chat.pinnedPosition === null) return
  dragged.value = chat.chatId
  event.dataTransfer?.setData('text/plain', chat.chatId)
}

function onDragOver(chat: ChatListItem, event: DragEvent): void {
  if (dragged.value && chat.pinnedPosition !== null) event.preventDefault()
}

function onDrop(target: ChatListItem): void {
  const moving = dragged.value
  dragged.value = null
  if (!moving || moving === target.chatId) return
  const order = chats.mainList.filter((c) => c.pinnedPosition !== null).map((c) => c.chatId)
  const without = order.filter((id) => id !== moving)
  without.splice(without.indexOf(target.chatId), 0, moving)
  void store.reorderPinned(without)
}

const found = ref<ChatListItem[]>([])
const isSearching = ref(false)

const debouncedQuery = useDebounced(() => props.query)

/**
 * The previous search is aborted: responses can arrive out of order, and a stale one would
 * otherwise overwrite the results for the current query.
 */
let inFlight: AbortController | null = null

watch(debouncedQuery, async (query) => {
  inFlight?.abort()

  const trimmed = query.trim()
  if (trimmed.length === 0) {
    found.value = []
    isSearching.value = false
    return
  }

  const controller = new AbortController()
  inFlight = controller
  isSearching.value = true

  try {
    const response = await chatsApi.searchChats(trimmed, controller.signal)
    found.value = response.items
  } catch {
    if (!controller.signal.aborted) found.value = []
  } finally {
    if (!controller.signal.aborted) isSearching.value = false
  }
})

</script>

<template>
  <section class="panel">
    <h2 class="heading">
      <button v-if="showArchive && !query.trim()" type="button" class="back" @click="showArchive = false">←</button>
      {{ query.trim() ? 'Найденные чаты' : showArchive ? 'Архив' : 'Чаты' }}
    </h2>

    <p v-if="!chats.loaded" class="note">загрузка…</p>

    <template v-else-if="query.trim()">
      <p v-if="isSearching" class="note">ищем…</p>
      <p v-else-if="found.length === 0" class="note">ничего не нашлось</p>
      <ChatRow
        v-for="chat in found"
        :key="chat.chatId"
        :chat="chat"
        :active="chat.chatId === store.selectedChatId"
        @click="store.select(chat.chatId)"
      />
    </template>

    <template v-else>
      <button
        v-if="!showArchive && chats.archivedList.length > 0"
        type="button"
        class="archive-row"
        @click="showArchive = true"
      >
        🗄 Архив <span class="archive-count">{{ chats.archivedList.length }}</span>
        <span v-if="archiveUnread > 0" class="archive-unread">{{ archiveUnread }}</span>
      </button>
      <p v-if="visible.length === 0" class="note">{{ showArchive ? 'архив пуст' : 'пока ни одного чата' }}</p>
      <ChatRow
        v-for="chat in visible"
        :key="chat.chatId"
        :chat="chat"
        :active="chat.chatId === store.selectedChatId"
        :draggable="chat.pinnedPosition !== null"
        @click="store.select(chat.chatId)"
        @contextmenu.prevent="openMenu(chat, $event)"
        @dragstart="onDragStart(chat, $event)"
        @dragover="onDragOver(chat, $event)"
        @drop.prevent="onDrop(chat)"
        @dragend="dragged = null"
      />
    </template>

    <ChatMenu v-if="menu" :chat="menu.chat" :x="menu.x" :y="menu.y" @close="menu = null" />
  </section>
</template>

<style scoped>
.panel {
  display: grid;
  align-content: start;
}
.heading {
  margin: 0;
  padding: 10px 12px 6px;
  color: var(--text-faint);
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.7px;
  text-transform: uppercase;
}
.back {
  margin-right: 6px;
  padding: 0 6px;
  border: none;
  background: none;
  color: var(--text-dim);
}
.archive-row {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  padding: 8px 12px;
  border: none;
  background: transparent;
  color: var(--text-dim);
  text-align: left;
}
.archive-row:hover {
  background: var(--surface-hover);
}
.archive-count {
  color: var(--text-faint);
  font-size: 12px;
}
.archive-unread {
  margin-left: auto;
  min-width: 18px;
  padding: 1px 5px;
  border-radius: 9px;
  background: var(--text-faint);
  color: var(--bg);
  font-size: 11px;
  font-weight: 700;
  text-align: center;
}
.note {
  margin: 0;
  padding: 6px 12px 10px;
  color: var(--text-dim);
  font-size: 12px;
}
.note.error {
  color: var(--danger);
}
</style>
