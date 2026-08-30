<script setup lang="ts">
import { computed } from 'vue'

import type { ChatListItem } from '@/entities/chat/types'
import { chatInitial, chatTitle } from '@/entities/chat/lib'
import { usePresenceStore } from '@/entities/user/presence.store'
import { formatTime } from '@/shared/lib/date'

const props = defineProps<{
  chat: ChatListItem
  active: boolean
}>()

const presence = usePresenceStore()

const title = computed(() => chatTitle(props.chat))

// Точку показываем только у приватных чатов: у группы нет одного собеседника.
const isCompanionOnline = computed(
  () => props.chat.type === 'private' && presence.isOnline(props.chat.companionId),
)

const isTyping = computed(() => presence.isSomeoneTyping(props.chat.chatId))
const initial = computed(() => chatInitial(props.chat))

const preview = computed(() => {
  if (isTyping.value) return 'печатает…'

  const message = props.chat.lastMessage
  if (!message) return 'нет сообщений'
  return `${message.senderName}: ${message.text}`
})

const time = computed(() =>
  props.chat.lastMessage ? formatTime(props.chat.lastMessage.createdAt) : '',
)
</script>

<template>
  <button type="button" :class="['row', { active }]">
    <span class="avatar">
      {{ initial }}
      <span v-if="isCompanionOnline" class="online" title="в сети" />
    </span>

    <span class="middle">
      <!-- Всё через интерполяцию: имена и тексты приходят от других пользователей. -->
      <span class="title">{{ title }}</span>
      <span :class="['preview', { typing: isTyping }]">{{ preview }}</span>
    </span>

    <span class="right">
      <span class="time">{{ time }}</span>
      <span v-if="chat.unreadCount > 0" class="badge">{{ chat.unreadCount }}</span>
    </span>
  </button>
</template>

<style scoped>
.row {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  gap: 10px;
  width: 100%;
  padding: 9px 12px;
  border: none;
  border-left: 2px solid transparent;
  background: transparent;
  color: inherit;
  text-align: left;
}
.row:hover {
  background: var(--surface-hover);
}
.row.active {
  border-left-color: var(--accent);
  background: var(--accent-soft);
}
.avatar {
  position: relative;
  display: grid;
  place-items: center;
  width: 34px;
  height: 34px;
  border-radius: 50%;
  background: var(--surface-hover);
  color: var(--accent);
  font-weight: 700;
}
.online {
  position: absolute;
  right: -1px;
  bottom: -1px;
  width: 9px;
  height: 9px;
  border: 2px solid var(--surface-solid);
  border-radius: 50%;
  background: var(--accent);
}
.middle {
  display: grid;
  gap: 2px;
  min-width: 0;
}
.title {
  overflow: hidden;
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.preview.typing {
  color: var(--accent);
  font-style: italic;
}
.preview {
  overflow: hidden;
  color: var(--text-dim);
  font-size: 12px;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.right {
  display: grid;
  gap: 4px;
  justify-items: end;
}
.time {
  color: var(--text-faint);
  font-size: 11px;
}
.badge {
  min-width: 18px;
  padding: 1px 5px;
  border-radius: 9px;
  background: var(--accent);
  color: #04160b;
  font-size: 11px;
  font-weight: 700;
  text-align: center;
}
</style>
