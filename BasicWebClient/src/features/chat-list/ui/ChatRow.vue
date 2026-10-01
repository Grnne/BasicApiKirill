<script setup lang="ts">
import { computed } from 'vue'

import type { ChatListItem } from '@/entities/chat/types'
import { chatInitial, chatTitle, isMutedNow } from '@/entities/chat/lib'
import AvatarCircle from '@/entities/media/ui/AvatarCircle.vue'
import { usePresenceStore } from '@/entities/user/presence.store'
import { messagePreview } from '@/entities/message/lib/preview'
import { STATUS_MARKS, ownStatus } from '@/entities/message/lib/status'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { formatTime } from '@/shared/lib/date'

// An absent boolean prop would be cast to false: null means "the global pin".
const props = withDefaults(
  defineProps<{
    chat: ChatListItem
    active: boolean
    /** Pinned where the list is shown: globally, or inside the open folder. */
    pinned?: boolean | null
  }>(),
  { pinned: null },
)

const presence = usePresenceStore()
const auth = useAuthStore()

const title = computed(() => chatTitle(props.chat))

const isCompanionOnline = computed(
  () => props.chat.type === 'private' && presence.isOnline(props.chat.companionId),
)

const muted = computed(() => isMutedNow(props.chat))

const isTyping = computed(() => presence.isSomeoneTyping(props.chat.chatId))
const initial = computed(() => chatInitial(props.chat))

const preview = computed(() => {
  if (isTyping.value) return 'печатает…'
  if (props.chat.draft && !props.active) return props.chat.draft.text

  const message = props.chat.lastMessage
  if (!message) return 'нет сообщений'
  return messagePreview(message, auth.user?.userId ?? null, props.chat.type === 'group')
})

const status = computed(() =>
  props.chat.lastMessage ? ownStatus(props.chat.lastMessage, props.chat, auth.user?.userId ?? null) : null,
)

const time = computed(() =>
  props.chat.lastMessage ? formatTime(props.chat.lastMessage.createdAt) : '',
)
</script>

<template>
  <button type="button" :class="['row', { active }]">
    <span class="avatar">
      <AvatarCircle :avatar-id="chat.avatarId" :initial="initial" />
      <span v-if="isCompanionOnline" class="online" title="в сети" />
    </span>

    <span class="middle">
      <!-- Interpolation only: names and texts come from other users. -->
      <span class="title">
        <span v-if="pinned ?? chat.pinnedPosition !== null" class="pin" title="Закреплён">📌</span>{{ title }}<span
          v-if="muted"
          class="muted"
          title="Без звука"
          >🔕</span
        >
      </span>
      <span :class="['preview', { typing: isTyping }]">
        <span v-if="chat.draft && !active && !isTyping" class="draft">Черновик: </span>{{ preview }}
      </span>
    </span>

    <span class="right">
      <span class="time">
        <span v-if="status" :class="['status', status]" :title="STATUS_MARKS[status].title">
          {{ STATUS_MARKS[status].mark }}
        </span>
        {{ time }}
      </span>
      <span v-if="!active" class="badges">
        <span v-if="chat.unreadMentionCount > 0" class="badge mention" title="Вас упомянули">@</span>
        <span v-if="chat.unreadCount > 0" :class="['badge', { quiet: muted }]">{{ chat.unreadCount }}</span>
        <span v-else-if="chat.markedUnread" class="badge dot" title="Помечен непрочитанным" />
      </span>
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
  align-self: center;
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
.muted {
  margin-left: 4px;
  font-size: 11px;
}
.badge.quiet {
  background: var(--text-faint);
}
.pin {
  margin-right: 4px;
  font-size: 11px;
}
.draft {
  color: var(--danger);
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
.badges {
  display: flex;
  gap: 3px;
}
.badge.dot {
  min-width: 10px;
  height: 10px;
  margin-top: 4px;
  padding: 0;
}
.status {
  margin-right: 2px;
  letter-spacing: -3px;
}
.status.read {
  color: var(--accent);
}
</style>
