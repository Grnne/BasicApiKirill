<script setup lang="ts">
import { computed } from 'vue'

import { useChatDetailsStore } from '@/entities/chat/model/details.store'
import { STATUS_MARKS, type OwnStatus } from '@/entities/message/lib/status'
import { systemCaption } from '@/entities/message/lib/system'
import type { Message } from '@/entities/message/types'
import FormattedText from '@/entities/message/ui/FormattedText'
import AttachmentList from '@/entities/media/ui/AttachmentList.vue'
import { formatTime } from '@/shared/lib/date'

const props = defineProps<{
  message: Message
  own: boolean
  meId: string | null
  status?: OwnStatus | null
}>()
defineEmits<{ jump: [messageId: string]; react: [emoji: string] }>()

const files = computed(() => props.message.attachments.length)

const details = useChatDetailsStore()
const caption = computed(() =>
  props.message.type === 'system'
    ? systemCaption(props.message, (id) => details.member(props.message.chatId, id)?.displayName ?? null)
    : '',
)
</script>

<template>
  <!-- A record of what happened in a group, with the names people have now. -->
  <p v-if="message.type === 'system'" class="system">{{ caption }}</p>

  <article v-else :class="['bubble', { own }]">
    <span v-if="!own" class="sender">{{ message.senderName }}</span>
    <span v-if="message.forwardFrom" class="forwarded">Переслано от {{ message.forwardFrom.senderName }}</span>
    <button
      v-if="message.replyTo"
      type="button"
      class="reply"
      title="К исходному сообщению"
      :disabled="message.replyTo.deleted"
      @click="$emit('jump', message.replyTo.messageId)"
    >
      <span class="reply-sender">{{ message.replyTo.senderName }}</span>
      <span class="reply-text">{{ message.replyTo.deleted ? 'Сообщение удалено' : message.replyTo.text }}</span>
    </button>
    <AttachmentList v-if="files > 0" :attachments="message.attachments" />
    <p v-if="message.text" class="text">
      <FormattedText :text="message.text" :entities="message.entities" :me-id="meId" />
    </p>
    <div v-if="message.reactions.length > 0" class="reactions">
      <button
        v-for="r in message.reactions"
        :key="r.emoji"
        type="button"
        :class="['reaction', { mine: r.emoji === message.myReaction }]"
        :title="r.emoji === message.myReaction ? 'Убрать реакцию' : 'Поставить реакцию'"
        @click="$emit('react', r.emoji)"
      >
        {{ r.emoji }} {{ r.count }}
      </button>
    </div>
    <span class="time">
      <span v-if="message.editedAt" class="edited">изменено</span>
      {{ formatTime(message.createdAt) }}
      <span v-if="status" :class="['status', status]" :title="STATUS_MARKS[status].title">
        {{ STATUS_MARKS[status].mark }}
      </span>
    </span>
  </article>
</template>

<style scoped>
.bubble {
  max-width: min(560px, 75%);
  align-self: flex-start;
  padding: 7px 11px;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  border-bottom-left-radius: 2px;
  background: var(--surface-solid);
}
.bubble.own {
  align-self: flex-end;
  border-color: transparent;
  border-radius: var(--radius);
  border-bottom-right-radius: 2px;
  background: var(--accent-soft);
}
.sender {
  display: block;
  margin-bottom: 2px;
  color: var(--accent);
  font-size: 12px;
  font-weight: 600;
}
.text {
  margin: 0;
  overflow-wrap: anywhere;
  white-space: pre-wrap;
}
.forwarded {
  display: block;
  margin-bottom: 2px;
  color: var(--text-dim);
  font-size: 12px;
  font-style: italic;
}
.reply {
  display: grid;
  width: 100%;
  margin: 2px 0 4px;
  padding: 3px 8px;
  border: none;
  border-left: 2px solid var(--accent);
  border-radius: var(--radius-sm);
  background: var(--surface-hover);
  color: var(--text);
  text-align: left;
}
.reply:disabled {
  cursor: default;
}
.reply-sender {
  color: var(--accent);
  font-size: 12px;
  font-weight: 600;
}
.reply-text {
  overflow: hidden;
  color: var(--text-dim);
  font-size: 12px;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.reactions {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  margin-top: 4px;
}
.reaction {
  padding: 1px 7px;
  border: 1px solid var(--border);
  border-radius: 10px;
  background: var(--surface-hover);
  color: var(--text);
  font-size: 12px;
}
.reaction.mine {
  border-color: var(--accent);
  background: var(--accent-soft);
}
.system {
  align-self: center;
  max-width: 80%;
  margin: 4px 0;
  padding: 3px 12px;
  border-radius: 10px;
  background: var(--surface-hover);
  color: var(--text-dim);
  font-size: 12px;
  text-align: center;
}
.status {
  margin-left: 3px;
  letter-spacing: -3px;
}
.status.read {
  color: var(--accent);
}
.edited {
  margin-right: 4px;
  font-style: italic;
}
.text :deep(code) {
  padding: 0 4px;
  border-radius: 4px;
  background: var(--surface-hover);
  font-family: ui-monospace, Consolas, monospace;
  font-size: 0.92em;
}
.text :deep(pre) {
  margin: 4px 0;
  padding: 6px 8px;
  overflow-x: auto;
  border-radius: var(--radius-sm);
  background: var(--surface-hover);
  white-space: pre;
}
.text :deep(pre code) {
  padding: 0;
  background: none;
}
.text :deep(a) {
  color: var(--accent);
  text-decoration: underline;
}
.text :deep(.mention) {
  color: var(--accent);
  font-weight: 600;
}
.text :deep(.mention.me) {
  padding: 0 2px;
  border-radius: 4px;
  background: var(--accent-soft);
}
.text :deep(.spoiler:not(.revealed)) {
  border-radius: 4px;
  background: var(--text-dim);
  color: transparent;
  cursor: pointer;
  user-select: none;
}
.time {
  display: block;
  margin-top: 2px;
  color: var(--text-faint);
  font-size: 10px;
  text-align: right;
}
</style>
