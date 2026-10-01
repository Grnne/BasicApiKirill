<script setup lang="ts">
import { computed, nextTick, onUnmounted, ref, watch } from 'vue'

import { useChatsStore } from '@/entities/chat/model/chats.store'
import { useChatDetailsStore } from '@/entities/chat/model/details.store'
import { useConfigStore } from '@/entities/config/config.store'
import { ownStatus } from '@/entities/message/lib/status'
import { messageActions } from '@/entities/message/lib/actions'
import AvatarCircle from '@/entities/media/ui/AvatarCircle.vue'
import type { Message } from '@/entities/message/types'
import ConfirmDialog from '@/shared/ui/ConfirmDialog.vue'
import { useNoticesStore } from '@/shared/ui/notices.store'
import { opensBelow } from '../lib/menu'
import { useMessagesStore } from '../model/messages.store'
import ForwardDialog from './ForwardDialog.vue'
import MessageBubble from './MessageBubble.vue'

const props = withDefaults(
  defineProps<{
    message: Message
    meId: string | null
    /** Groups: the sender's photo by the last message of their run, its place by the others. */
    avatar?: 'show' | 'space' | null
  }>(),
  { avatar: null },
)

const store = useMessagesStore()
const config = useConfigStore()
const notices = useNoticesStore()
const chats = useChatsStore()
const details = useChatDetailsStore()
const status = computed(() => ownStatus(props.message, chats.get(props.message.chatId), props.meId))

const own = computed(() => props.message.senderId === props.meId)
const sender = computed(() =>
  props.avatar === 'show' ? details.member(props.message.chatId, props.message.senderId) : null,
)
const system = computed(() => props.message.type === 'system')
const actions = computed(() =>
  messageActions(props.message, {
    meId: props.meId,
    now: Date.now(),
    editWindowHours: config.config.messages.editWindowHours,
    deleteWindowHours: config.config.messages.deleteWindowHours,
  }),
)

const selecting = computed(() => store.selected.size > 0)
const isSelected = computed(() => store.selected.has(props.message.id))

const menuOpen = ref(false)
const menuBelow = ref(false)
const menu = ref<HTMLElement | null>(null)

async function toggleMenu(event: MouseEvent): Promise<void> {
  menuOpen.value = !menuOpen.value
  if (!menuOpen.value) return
  menuBelow.value = false
  await nextTick()
  const button = event.currentTarget as HTMLElement | null
  const list = button?.closest('.viewport')
  if (!button || !list || !menu.value) return
  menuBelow.value = opensBelow(button.getBoundingClientRect(), list.getBoundingClientRect(), menu.value.offsetHeight)
}
const forwarding = ref(false)
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

function react(emoji: string): void {
  void store.react(props.message, emoji)
  close()
}

function reply(): void {
  store.startReply(props.message)
  close()
}

function select(): void {
  store.toggleSelected(props.message.id)
  close()
}

function openForward(): void {
  forwarding.value = true
  close()
}

async function forwardTo(chatId: string): Promise<void> {
  forwarding.value = false
  const count = await store.forward(chatId, [props.message.id])
  if (count > 0) notices.push('Сообщение переслано', 'info')
}

/** In selection mode a click picks the message instead of acting on its content. */
function onClick(event: MouseEvent): void {
  if (!selecting.value) return
  event.preventDefault()
  store.toggleSelected(props.message.id)
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

  <div
    v-else
    :class="['item', { own, selected: isSelected, selecting }]"
    :data-message-id="message.id"
    @click.capture="onClick"
  >
    <span v-if="selecting" class="check" aria-hidden="true">{{ isSelected ? '✓' : '' }}</span>
    <template v-if="avatar && !own">
      <AvatarCircle
        v-if="avatar === 'show'"
        :avatar-id="sender?.avatarId ?? null"
        :initial="(sender?.displayName ?? message.senderName).charAt(0).toUpperCase() || '?'"
        :size="28"
      />
      <span v-else class="avatar-space" />
    </template>
    <MessageBubble
      :message="message"
      :own="own"
      :me-id="meId"
      :status="status"
      @jump="store.jumpTo($event)"
      @react="store.react(message, $event)"
    />
    <div v-if="!selecting" class="tools">
      <button
        type="button"
        class="more"
        title="Действия"
        aria-haspopup="menu"
        :aria-expanded="menuOpen"
        @click.stop="toggleMenu"
      >
        ⋯
      </button>
      <ul v-if="menuOpen" ref="menu" :class="['menu', { below: menuBelow }]" role="menu">
        <li v-if="actions.react" class="emoji-row">
          <button
            v-for="emoji in config.config.messages.reactions"
            :key="emoji"
            type="button"
            :class="['emoji', { mine: emoji === message.myReaction }]"
            :title="emoji === message.myReaction ? 'Убрать реакцию' : 'Поставить реакцию'"
            @click="react(emoji)"
          >
            {{ emoji }}
          </button>
        </li>
        <li v-if="actions.reply"><button type="button" role="menuitem" @click="reply">Ответить</button></li>
        <li v-if="actions.forward"><button type="button" role="menuitem" @click="openForward">Переслать</button></li>
        <li v-if="actions.forward"><button type="button" role="menuitem" @click="select">Выбрать</button></li>
        <li v-if="actions.edit"><button type="button" role="menuitem" @click="edit">Изменить</button></li>
        <li v-if="actions.copy"><button type="button" role="menuitem" @click="copy">Копировать текст</button></li>
        <li v-if="actions.deleteForMe">
          <button type="button" role="menuitem" class="danger" @click="askDelete">Удалить</button>
        </li>
      </ul>
    </div>

    <ForwardDialog v-if="forwarding" :count="1" @pick="forwardTo" @cancel="forwarding = false" />

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
.avatar-space {
  flex-shrink: 0;
  width: 28px;
}
.item.selecting {
  cursor: pointer;
}
.item.selected :deep(.bubble) {
  outline: 2px solid var(--accent);
}
.check {
  display: grid;
  place-items: center;
  width: 18px;
  height: 18px;
  flex: none;
  border: 1px solid var(--accent);
  border-radius: 50%;
  color: var(--accent);
  font-size: 12px;
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
.menu.below {
  top: 100%;
  bottom: auto;
  margin: 4px 0 0;
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
.menu .emoji-row {
  display: flex;
  flex-wrap: wrap;
  gap: 2px;
  max-width: 220px;
  padding-bottom: 4px;
  border-bottom: 1px solid var(--border);
  margin-bottom: 4px;
}
.menu .emoji {
  width: auto;
  padding: 3px 5px;
  font-size: 17px;
}
.menu .emoji.mine {
  background: var(--accent-soft);
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
