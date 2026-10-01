<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'

import { useChatsStore } from '@/entities/chat/model/chats.store'
import { useChatDetailsStore } from '@/entities/chat/model/details.store'
import type { ChatParticipant } from '@/entities/chat/types'
import { useConfigStore } from '@/entities/config/config.store'
import { safeUrl } from '@/entities/message/lib/formatted'
import FormattedText from '@/entities/message/ui/FormattedText'
import * as usersApi from '@/entities/user/api'
import { useAccountStore } from '@/entities/user/model/account.store'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { describeError } from '@/shared/api/problem'
import * as messagesApi from '../api/messages.api'
import { adjustEntities, insertMention, mentionQuery, setLink, toggleEntity, type Entity } from '../lib/compose'
import { useUploads } from '../lib/useUploads'
import { sameDraft, type DraftContent } from '../model/drafts'
import { useMessagesStore } from '../model/messages.store'
import UploadTray from './UploadTray.vue'

/**
 * Typing has a per-user limit of its own, shared with drafts (20 per 10 s): repeat it only while
 * typing goes on; the server drops it after 6 s without a repeat.
 */
const TYPING_THROTTLE_MS = 3_000

const TYPING_STOP_DELAY_MS = 3_000

const MAX_SUGGESTIONS = 6

const FORMATS = [
  { type: 'bold', label: 'Ж', title: 'Жирный (Ctrl+B)' },
  { type: 'italic', label: 'К', title: 'Курсив (Ctrl+I)' },
  { type: 'underline', label: 'Ч', title: 'Подчёркнутый (Ctrl+U)' },
  { type: 'strikethrough', label: 'З', title: 'Зачёркнутый (Ctrl+Shift+X)' },
  { type: 'code', label: '</>', title: 'Моноширинный (Ctrl+Shift+M)' },
  { type: 'spoiler', label: '▒', title: 'Скрытый (Ctrl+Shift+P)' },
] as const

const HOTKEYS: Record<string, string> = {
  b: 'bold',
  i: 'italic',
  u: 'underline',
  'shift+x': 'strikethrough',
  'shift+m': 'code',
  'shift+p': 'spoiler',
}

const store = useMessagesStore()
const config = useConfigStore()
const chats = useChatsStore()
const details = useChatDetailsStore()
const auth = useAuthStore()

const text = ref('')
const entities = ref<Entity[]>([])
const input = ref<HTMLTextAreaElement | null>(null)
const hasSelection = ref(false)

const uploads = useUploads()

/** A group may be read-only for members, or take no files from them; the server checks again. */
const groupRights = computed(() => {
  const chatId = store.chatId
  if (!chatId || chats.get(chatId)?.type !== 'group') return null
  return details.get(chatId)?.myPermissions ?? null
})
const mayWrite = computed(() => groupRights.value?.sendMessages !== false)

const account = useAccountStore()
/** The other side of a private chat. */
const companionId = computed(() => {
  const chat = chats.get(store.chatId)
  return chat?.type === 'private' ? chat.companionId : null
})
const blockedByMe = computed(() => account.isBlocked(companionId.value))
const unblockError = ref<string | null>(null)

async function unblock(): Promise<void> {
  const userId = companionId.value
  if (!userId) return
  unblockError.value = null
  try {
    await usersApi.unblockUser(userId)
    account.apply('BlockListChanged', { userId, blocked: false })
  } catch (e) {
    unblockError.value = describeError(e)
  }
}
const mayAttach = computed(() => groupRights.value?.sendMedia !== false)
const fileInput = ref<HTMLInputElement | null>(null)

const maxLength = computed(() => config.config.messages.maxLength)
/** Text, or files that all finished uploading (then the text is an optional caption). */
const canSend = computed(() => {
  if (uploads.items.value.length > 0) return uploads.ready.value
  return text.value.trim().length > 0
})

function onFilesChosen(event: Event): void {
  const input = event.target as HTMLInputElement
  if (input.files) uploads.add(input.files)
  input.value = ''
}

function onPaste(event: ClipboardEvent): void {
  const files = event.clipboardData?.files
  if (!files || files.length === 0 || store.editing || !mayAttach.value) return
  event.preventDefault()
  uploads.add(files)
}

function onDrop(event: DragEvent): void {
  const files = event.dataTransfer?.files
  if (files && files.length > 0 && !store.editing && mayAttach.value) uploads.add(files)
}

function setContent(nextText: string, nextEntities: readonly Entity[]): void {
  text.value = nextText
  entities.value = [...nextEntities]
}

// Editing puts the message into the field; what was typed before comes back after.
let draftBeforeEdit: { text: string; entities: Entity[] } | null = null
watch(
  () => store.editing,
  async (message, previous) => {
    if (message) {
      if (!previous) draftBeforeEdit = { text: text.value, entities: entities.value }
      setContent(message.text, message.entities)
      await nextTick()
      input.value?.focus()
    } else if (previous) {
      setContent(draftBeforeEdit?.text ?? '', draftBeforeEdit?.entities ?? [])
      draftBeforeEdit = null
    }
  },
)

watch(
  () => store.replyTo,
  async (message) => {
    if (!message) return
    await nextTick()
    input.value?.focus()
  },
)

/* ── Draft ── */

const content = (): DraftContent => ({
  text: text.value,
  entities: entities.value,
  replyToMessageId: store.replyTo?.id ?? null,
})

/** A reply of the loaded draft whose message is not in the loaded history yet. */
let replyToRestore: string | null = null

function restoreReply(): void {
  if (!replyToRestore) return
  const message = store.messages.find((m) => m.id === replyToRestore)
  if (message) {
    replyToRestore = null
    store.startReply(message)
  }
}

function loadDraft(chatId: string): void {
  const draft = chats.get(chatId)?.draft ?? null
  setContent(draft?.text ?? '', draft?.entities ?? [])
  replyToRestore = draft?.replyToMessageId ?? null
  restoreReply()
}

// Leaving a chat saves its draft at once; the next chat's draft fills the field.
watch(
  () => store.chatId,
  (chatId, previous) => {
    if (previous) void store.drafts.flush(previous)
    // Files chosen in one chat do not travel to another.
    for (const item of [...uploads.items.value]) uploads.remove(item.key)
    if (chatId) loadDraft(chatId)
  },
  { immediate: true },
)

watch(() => store.messages.length, restoreReply)

// Every change of what is typed (text, formatting, reply) is saved after a pause.
watch([text, entities, () => store.replyTo], () => {
  const chatId = store.chatId
  if (!chatId || store.editing) return
  const current = content()
  if (!store.drafts.isDirty(chatId) && sameDraft(current, chats.get(chatId)?.draft ?? null)) return
  store.drafts.schedule(chatId, current)
})

// A draft saved on another device replaces the field only when nothing unsaved is typed here.
watch(
  () => (store.chatId ? chats.get(store.chatId)?.draft : undefined),
  (draft) => {
    const chatId = store.chatId
    if (!chatId || draft === undefined || store.editing || store.drafts.isDirty(chatId)) return
    if (!sameDraft(content(), draft)) loadDraft(chatId)
  },
)

function onHidden(): void {
  if (document.visibilityState === 'hidden') store.drafts.flushAll()
}
onMounted(() => document.addEventListener('visibilitychange', onHidden))
onUnmounted(() => {
  document.removeEventListener('visibilitychange', onHidden)
  store.drafts.flushAll()
})

/* ── Typing ── */

let lastTypingSentAt = 0
let stopTypingTimer: ReturnType<typeof setTimeout> | undefined

function typing(chatId: string, isTyping: boolean): void {
  messagesApi.sendTyping(chatId, isTyping).catch(() => {
    // Typing is cosmetic: a lost one is not worth an error.
  })
}

function stopTyping(): void {
  clearTimeout(stopTypingTimer)
  stopTypingTimer = undefined

  const chatId = store.chatId
  if (chatId && lastTypingSentAt > 0) {
    lastTypingSentAt = 0
    typing(chatId, false)
  }
}

function noteTyping(): void {
  const chatId = store.chatId
  if (!chatId || store.editing) return

  const now = Date.now()
  if (now - lastTypingSentAt > TYPING_THROTTLE_MS) {
    lastTypingSentAt = now
    typing(chatId, true)
  }
  clearTimeout(stopTypingTimer)
  stopTypingTimer = setTimeout(stopTyping, TYPING_STOP_DELAY_MS)
}

onUnmounted(stopTyping)

/* ── Text and formatting ── */

function onInput(event: Event): void {
  const next = (event.target as HTMLTextAreaElement).value
  entities.value = adjustEntities(entities.value, text.value, next)
  text.value = next
  noteTyping()
  updateMention()
}

function onSelect(): void {
  const el = input.value
  hasSelection.value = !!el && el.selectionEnd > el.selectionStart
  updateMention()
}

async function keepSelection(from: number, to: number): Promise<void> {
  await nextTick()
  input.value?.focus()
  input.value?.setSelectionRange(from, to)
}

function format(type: string): void {
  const el = input.value
  if (!el || el.selectionEnd <= el.selectionStart) return
  const { selectionStart: from, selectionEnd: to } = el
  entities.value = toggleEntity(entities.value, type, from, to)
  void keepSelection(from, to)
}

/** The link being set: the selection it covers and the address typed so far. */
const linkDraft = ref<{ from: number; to: number; url: string } | null>(null)

function startLink(): void {
  const el = input.value
  if (!el || el.selectionEnd <= el.selectionStart) return
  const { selectionStart: from, selectionEnd: to } = el
  const existing = entities.value.find(
    (e) => e.type === 'link' && e.offset <= from && e.offset + e.length >= to,
  )
  linkDraft.value = { from, to, url: existing?.url ?? '' }
}

function applyLink(): void {
  const draft = linkDraft.value
  if (!draft) return
  const raw = draft.url.trim()
  // "example.com" means the site, not a relative path.
  const url = raw === '' ? null : (safeUrl(raw) ?? safeUrl(`https://${raw}`))
  if (raw !== '' && !url) return
  entities.value = setLink(entities.value, draft.from, draft.to, url)
  linkDraft.value = null
  void keepSelection(draft.to, draft.to)
}

/* ── Mentions ── */

const members = computed<ChatParticipant[]>(() => {
  const chatId = store.chatId
  if (!chatId || chats.get(chatId)?.type !== 'group') return []
  // Not loaded yet or failed: no suggestions, a name can still be typed as text.
  return details.get(chatId)?.participants ?? []
})
const mention = ref<{ start: number; query: string } | null>(null)
const activeSuggestion = ref(0)

const suggestions = computed(() => {
  const m = mention.value
  if (!m) return []
  const q = m.query.toLowerCase()
  return members.value
    .filter((p) => p.userId !== auth.user?.userId)
    .filter((p) => p.displayName.toLowerCase().includes(q) || p.username.toLowerCase().startsWith(q))
    .slice(0, MAX_SUGGESTIONS)
})

function updateMention(): void {
  const el = input.value
  const caret = el && el.selectionStart === el.selectionEnd ? el.selectionStart : -1
  // Kept without members too: they may still be loading, the suggestions follow when they come.
  mention.value = caret >= 0 ? mentionQuery(text.value, caret) : null
  activeSuggestion.value = 0
}

function pickMention(member: ChatParticipant): void {
  const m = mention.value
  const el = input.value
  if (!m || !el) return
  const result = insertMention(text.value, entities.value, m.start, el.selectionStart, member)
  setContent(result.text, result.entities)
  mention.value = null
  void keepSelection(result.caret, result.caret)
}

/* ── Sending ── */

async function submit(): Promise<void> {
  if (!canSend.value) return
  if (store.editing) {
    await store.saveEdit(text.value, entities.value)
    return
  }
  // Sending failures show on the message itself, with a retry.
  if (store.send(text.value, entities.value, uploads.attachments.value)) {
    setContent('', [])
    uploads.clear()
    stopTyping()
  }
}

function onSuggestionKey(event: KeyboardEvent): boolean {
  const n = suggestions.value.length
  if (n === 0) return false
  if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    activeSuggestion.value = (activeSuggestion.value + (event.key === 'ArrowDown' ? 1 : n - 1)) % n
    return true
  }
  if (event.key === 'Enter' || event.key === 'Tab') {
    const member = suggestions.value[activeSuggestion.value]
    if (member) pickMention(member)
    return true
  }
  if (event.key === 'Escape') {
    mention.value = null
    return true
  }
  return false
}

function onFormatKey(event: KeyboardEvent): boolean {
  if (!event.ctrlKey && !event.metaKey) return false
  const key = `${event.shiftKey ? 'shift+' : ''}${event.key.toLowerCase()}`
  const type = HOTKEYS[key]
  if (type) {
    format(type)
    return true
  }
  if (key === 'k') {
    startLink()
    return true
  }
  return false
}

function onKeydown(event: KeyboardEvent): void {
  if (onSuggestionKey(event) || onFormatKey(event)) {
    event.preventDefault()
    return
  }
  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault()
    void submit()
  } else if (event.key === 'Escape') {
    if (store.editing) store.cancelEdit()
    else store.cancelReply()
  }
}
</script>

<template>
  <div v-if="blockedByMe" class="read-only">
    Вы заблокировали этого пользователя.
    <button type="button" class="inline" @click="unblock">Разблокировать</button>
    <span v-if="unblockError" class="inline-error">{{ unblockError }}</span>
  </div>
  <div v-else-if="store.isRestricted(store.chatId)" class="read-only">
    Пользователь ограничил, кто может ему писать.
    <button type="button" class="inline" @click="store.tryAgain(store.chatId!)">Попробовать ещё раз</button>
  </div>
  <p v-else-if="!mayWrite" class="read-only">В этой группе писать могут только админы</p>
  <form v-else class="composer" @submit.prevent="submit" @dragover.prevent @drop.prevent="onDrop">
    <div v-if="store.editing" class="context">
      <span class="label">Редактирование</span>
      <span class="quote">{{ store.editing.text }}</span>
      <button type="button" class="close" title="Отменить (Esc)" @click="store.cancelEdit()">✕</button>
    </div>
    <div v-else-if="store.replyTo" class="context">
      <span class="label">Ответ {{ store.replyTo.senderName }}</span>
      <span class="quote">{{ store.replyTo.text }}</span>
      <button type="button" class="close" title="Отменить (Esc)" @click="store.cancelReply()">✕</button>
    </div>

    <div class="tools" role="toolbar" aria-label="Форматирование">
      <button
        v-for="f in FORMATS"
        :key="f.type"
        type="button"
        class="tool"
        :title="f.title"
        :disabled="!hasSelection"
        @mousedown.prevent
        @click="format(f.type)"
      >
        {{ f.label }}
      </button>
      <button
        type="button"
        class="tool attach"
        :title="mayAttach ? 'Прикрепить файлы' : 'В этой группе файлы отправляют только админы'"
        :disabled="!!store.editing || !mayAttach"
        @click="fileInput?.click()"
      >
        📎
      </button>
      <input ref="fileInput" type="file" multiple hidden @change="onFilesChosen" />
      <button
        type="button"
        class="tool"
        title="Ссылка (Ctrl+K)"
        :disabled="!hasSelection"
        @mousedown.prevent
        @click="startLink"
      >
        🔗
      </button>
    </div>

    <div v-if="linkDraft" class="link">
      <input
        v-model="linkDraft.url"
        class="link-input"
        type="url"
        placeholder="https://… (пусто — убрать ссылку)"
        @keydown.enter.prevent="applyLink"
        @keydown.esc.prevent="linkDraft = null"
      />
      <button type="button" class="tool" @click="applyLink">OK</button>
    </div>

    <UploadTray v-if="uploads.items.value.length > 0" :items="uploads.items.value" @remove="uploads.remove" />

    <ul v-if="suggestions.length > 0" class="suggestions" role="listbox">
      <li v-for="(member, index) in suggestions" :key="member.userId">
        <button
          type="button"
          :class="['suggestion', { active: index === activeSuggestion }]"
          @mousedown.prevent
          @click="pickMention(member)"
        >
          {{ member.displayName }} <span class="username">@{{ member.username }}</span>
        </button>
      </li>
    </ul>

    <textarea
      ref="input"
      :value="text"
      class="input"
      rows="1"
      :maxlength="maxLength"
      placeholder="Написать сообщение"
      @input="onInput"
      @select="onSelect"
      @keyup="onSelect"
      @click="onSelect"
      @keydown="onKeydown"
      @paste="onPaste"
    />
    <button type="submit" class="send" :disabled="!canSend">
      {{ store.editing ? 'Сохранить' : 'Отправить' }}
    </button>

    <p v-if="entities.length > 0" class="preview">
      <FormattedText :text="text" :entities="entities" :me-id="auth.user?.userId ?? null" />
    </p>
  </form>
</template>

<style scoped>
.composer {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 8px;
  padding: 10px 12px;
  border-top: 1px solid var(--border);
  background: var(--surface-solid);
}
.context {
  grid-column: 1 / -1;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 4px 8px;
  border-left: 2px solid var(--accent);
  background: var(--surface-hover);
  font-size: 12px;
}
.label {
  color: var(--accent);
  font-weight: 600;
}
.quote {
  overflow: hidden;
  flex: 1;
  color: var(--text-dim);
  text-overflow: ellipsis;
  white-space: nowrap;
}
.close {
  border: none;
  background: none;
  color: var(--text-dim);
}
.tools {
  grid-column: 1 / -1;
  display: flex;
  gap: 2px;
}
.tool {
  min-width: 28px;
  padding: 2px 6px;
  border: 1px solid transparent;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-dim);
  font-size: 12px;
}
.tool:hover:not(:disabled) {
  border-color: var(--border);
  color: var(--text);
}
.read-only {
  margin: 0;
  padding: 14px 16px;
  border-top: 1px solid var(--border);
  color: var(--text-dim);
  text-align: center;
}
.inline {
  padding: 0;
  border: none;
  background: none;
  color: var(--accent);
}
.inline-error {
  display: block;
  color: var(--danger);
  font-size: 12px;
}
.tool.attach {
  margin-right: 6px;
}
.tool:disabled {
  opacity: 0.35;
  cursor: default;
}
.link {
  grid-column: 1 / -1;
  display: flex;
  gap: 6px;
}
.link-input {
  flex: 1;
  padding: 5px 8px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--bg);
}
.suggestions {
  grid-column: 1 / -1;
  margin: 0;
  padding: 4px;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--surface-solid);
  list-style: none;
}
.suggestion {
  display: block;
  width: 100%;
  padding: 5px 8px;
  border: none;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text);
  text-align: left;
}
.suggestion.active,
.suggestion:hover {
  background: var(--surface-hover);
}
.username {
  color: var(--text-dim);
  font-size: 12px;
}
.input {
  min-height: 38px;
  max-height: 140px;
  padding: 9px 11px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--bg);
  resize: vertical;
}
.input:focus {
  border-color: var(--accent);
  outline: none;
}
.send {
  padding: 0 16px;
  border: none;
  border-radius: var(--radius-sm);
  background: var(--accent);
  color: #04160b;
  font-weight: 600;
}
.send:disabled {
  background: var(--surface-hover);
  color: var(--text-faint);
  cursor: default;
}
.preview {
  grid-column: 1 / -1;
  margin: 0;
  padding: 4px 8px;
  border-radius: var(--radius-sm);
  background: var(--bg);
  overflow-wrap: anywhere;
  white-space: pre-wrap;
}
.preview :deep(code) {
  padding: 0 4px;
  background: var(--surface-hover);
  font-family: var(--font-mono);
}
.preview :deep(.mention),
.preview :deep(a) {
  color: var(--accent);
}
.preview :deep(.spoiler:not(.revealed)) {
  background: var(--text-dim);
}
</style>
