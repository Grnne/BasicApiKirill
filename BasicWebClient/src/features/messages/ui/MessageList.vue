<script setup lang="ts">
import { nextTick, ref, watch } from 'vue'

import { useAuthStore } from '@/features/auth/model/auth.store'
import { formatDay, parseApiDate } from '@/shared/lib/date'
import { useMessagesStore } from '../model/messages.store'
import MessageBubble from './MessageBubble.vue'

/** Насколько близко к низу пользователь должен быть, чтобы лента доскроллилась сама. */
const STICK_THRESHOLD_PX = 120

/** За сколько пикселей до верха начинаем подгружать старые сообщения. */
const LOAD_OLDER_THRESHOLD_PX = 150

const auth = useAuthStore()
const store = useMessagesStore()

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

/**
 * Подгрузка вверх с сохранением позиции.
 *
 * Если просто добавить сообщения в начало, содержимое уедет вниз и
 * пользователь потеряет место, где читал. Поэтому запоминаем высоту до
 * вставки и после неё сдвигаем прокрутку ровно на прирост.
 */
async function loadOlderKeepingPosition(): Promise<void> {
  const element = viewport.value
  if (!element || store.isLoadingOlder || !store.hasMore) return

  const heightBefore = element.scrollHeight
  await store.loadOlder()
  await nextTick()
  element.scrollTop += element.scrollHeight - heightBefore
}

function onScroll(): void {
  const element = viewport.value
  if (!element) return
  if (element.scrollTop < LOAD_OLDER_THRESHOLD_PX) void loadOlderKeepingPosition()
}

// Новое сообщение доскроллит ленту, только если пользователь и так внизу.
// Иначе он читает историю — дёргать его нельзя.
watch(
  () => store.messages.length,
  async (length, previousLength) => {
    const isAppend = length > previousLength
    const stick = isNearBottom()
    await nextTick()
    if (isAppend && stick) scrollToBottom()
  },
)

/**
 * Открытый чат — всегда в самый низ.
 *
 * Следим именно за окончанием загрузки, а не за сменой chatId: пока
 * isLoading === true, в ленте висит заглушка, и сообщения появляются в DOM
 * только после её снятия. Прокрутка по chatId срабатывала на пустом списке
 * и не давала ничего.
 */
watch(
  () => store.isLoading,
  async (isLoading) => {
    if (isLoading) return
    await nextTick()
    scrollToBottom()
  },
)

/** Разделитель дат: показываем, когда следующее сообщение уже из другого дня. */
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
        <MessageBubble :message="message" :own="message.senderId === auth.user?.userId" />
      </template>
    </template>
  </div>
</template>

<style scoped>
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
