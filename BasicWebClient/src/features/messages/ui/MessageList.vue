<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'

import { useChatsStore } from '@/entities/chat/model/chats.store'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { formatDay, parseApiDate } from '@/shared/lib/date'
import { closesRun } from '../lib/runs'
import { useMessagesStore } from '../model/messages.store'
import MessageItem from './MessageItem.vue'
import PendingBubble from './PendingBubble.vue'

const STICK_THRESHOLD_PX = 120

const LOAD_OLDER_THRESHOLD_PX = 150

const auth = useAuthStore()
const store = useMessagesStore()
const chats = useChatsStore()
const isGroup = computed(() => chats.get(store.chatId)?.type === 'group')

const viewport = ref<HTMLElement | null>(null)

function isNearBottom(): boolean {
  const element = viewport.value
  if (!element) return true
  const distance = element.scrollHeight - element.scrollTop - element.clientHeight
  return distance < STICK_THRESHOLD_PX
}

function scrollToBottom(): void {
  const element = viewport.value
  if (element) element.scrollTop = element.scrollHeight
}

// Prepending pushes the content down; shifting the scroll by the height growth keeps the
// reading position.
async function loadOlderKeepingPosition(): Promise<void> {
  const element = viewport.value
  if (!element || store.isLoadingOlder || !store.hasMore) return

  const heightBefore = element.scrollHeight
  await store.loadOlder()
  await nextTick()
  element.scrollTop += element.scrollHeight - heightBefore
}

/** The newest messages count as read only when someone can see them. */
function noteSeenIfVisible(): void {
  if (document.visibilityState === 'visible' && isNearBottom()) store.seen()
}

function onScroll(): void {
  const element = viewport.value
  if (!element) return
  if (element.scrollTop < LOAD_OLDER_THRESHOLD_PX) void loadOlderKeepingPosition()
  if (store.hasNewer && isNearBottom()) void store.loadNewer()
  noteSeenIfVisible()
}

onMounted(() => document.addEventListener('visibilitychange', noteSeenIfVisible))
onUnmounted(() => document.removeEventListener('visibilitychange', noteSeenIfVisible))

// Follow new messages only when already at the bottom, not while the user reads history.
// Only a list that already reached the newest message sticks to the bottom: in a window in the
// middle, sticking would load every newer page one after another.
watch(
  () => ({ length: store.messages.length + store.pending.length, window: store.hasNewer }),
  async (now, before) => {
    const isAppend = now.length > before.length
    const stick = isNearBottom()
    await nextTick()
    if (isAppend && stick && !before.window) {
      scrollToBottom()
      noteSeenIfVisible()
    }
  },
)

/**
 * Scroll to the bottom when loading ends, not on chatId change: while loading, a placeholder is
 * shown and the messages are not in the DOM yet.
 */
watch(
  () => store.isLoading,
  async (isLoading) => {
    if (isLoading) return
    await nextTick()
    scrollToBottom()
  },
)

// A reply quote or search asked for a message: bring it to the middle and flash it.
watch(
  () => store.jumpTarget,
  async (messageId) => {
    if (!messageId) return
    await nextTick()
    const element = viewport.value?.querySelector<HTMLElement>(`[data-message-id="${CSS.escape(messageId)}"]`)
    store.jumpTarget = null
    if (!element) return
    element.scrollIntoView({ block: 'center' })
    element.classList.remove('jump-highlight')
    void element.offsetWidth
    element.classList.add('jump-highlight')
  },
)

function startsNewDay(index: number): boolean {
  const current = store.messages[index]
  if (!current) return false
  if (index === 0) return true

  const previous = store.messages[index - 1]
  if (!previous) return true

  return parseApiDate(previous.createdAt).toDateString() !==
    parseApiDate(current.createdAt).toDateString()
}
</script>

<template>
  <div ref="viewport" class="viewport" @scroll.passive="onScroll">
    <p v-if="store.isLoading" class="note">загрузка…</p>
    <p v-else-if="store.error" class="note error">{{ store.error }}</p>

    <template v-else>
      <p v-if="store.isLoadingOlder" class="note">грузим историю…</p>
      <p v-else-if="!store.hasMore && store.messages.length > 0" class="note">начало переписки</p>
      <p v-else-if="store.messages.length === 0" class="note">пока ни одного сообщения</p>

      <template v-for="(message, index) in store.messages" :key="message.id">
        <p v-if="startsNewDay(index)" class="day">{{ formatDay(message.createdAt) }}</p>
        <MessageItem
          :message="message"
          :me-id="auth.user?.userId ?? null"
          :avatar="isGroup ? (closesRun(store.messages, index) ? 'show' : 'space') : null"
        />
      </template>

      <PendingBubble
        v-for="message in store.pending"
        :key="message.clientMessageId"
        :message="message"
        @retry="store.retry(message.clientMessageId)"
        @discard="store.discard(message.clientMessageId)"
      />

      <p v-if="store.isLoadingNewer" class="note">грузим новые…</p>
      <button v-if="store.hasNewer" type="button" class="to-latest" title="К последним сообщениям" @click="store.backToLatest()">
        ↓
      </button>
    </template>
  </div>
</template>

<style scoped>
.to-latest {
  position: sticky;
  bottom: 8px;
  align-self: flex-end;
  width: 36px;
  height: 36px;
  border: 1px solid var(--border);
  border-radius: 50%;
  background: var(--surface-solid);
  color: var(--text);
  box-shadow: 0 2px 8px #0008;
}
.viewport {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 14px 16px;
  overflow-y: auto;
}
.note {
  margin: 0;
  padding: 6px;
  color: var(--text-faint);
  font-size: 12px;
  text-align: center;
}
.note.error {
  color: var(--danger);
}
.day {
  margin: 10px auto 4px;
  padding: 2px 10px;
  border-radius: 10px;
  background: var(--surface-hover);
  color: var(--text-dim);
  font-size: 11px;
}
</style>
