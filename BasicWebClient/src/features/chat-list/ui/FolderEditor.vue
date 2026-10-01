<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'

import { chatTitle } from '@/entities/chat/lib'
import { useChatsStore } from '@/entities/chat/model/chats.store'
import type { FolderDto } from '@/shared/api/schema'
import { useChatListStore } from '../model/chat-list.store'

const MAX_TITLE = 64

const props = defineProps<{ folder: FolderDto | null }>()
const emit = defineEmits<{ close: [] }>()

const store = useChatListStore()
const chats = useChatsStore()

const title = ref(props.folder?.title ?? '')
const includePrivate = ref(props.folder?.includePrivate ?? false)
const includeGroups = ref(props.folder?.includeGroups ?? false)
const onlyUnread = ref(props.folder?.onlyUnread ?? false)
const chatIds = ref<Set<string>>(new Set(props.folder?.chatIds ?? []))
const busy = ref(false)

const canSave = computed(() => title.value.trim().length > 0 && !busy.value)

function toggle(id: string): void {
  const next = new Set(chatIds.value)
  if (next.has(id)) next.delete(id)
  else next.add(id)
  chatIds.value = next
}

async function save(): Promise<void> {
  if (!canSave.value) return
  busy.value = true
  const ok = await store.saveFolder(props.folder?.id ?? null, {
    title: title.value.trim(),
    includePrivate: includePrivate.value,
    includeGroups: includeGroups.value,
    onlyUnread: onlyUnread.value,
    chatIds: [...chatIds.value],
    // The server keeps every pinned chat in the folder: an unchecked one is unpinned too.
    pinnedChatIds: (props.folder?.pinnedChatIds ?? []).filter((id) => chatIds.value.has(id)),
  })
  busy.value = false
  if (ok) emit('close')
}

async function remove(): Promise<void> {
  if (!props.folder) return
  await store.deleteFolder(props.folder.id)
  emit('close')
}

function onKey(event: KeyboardEvent): void {
  if (event.key === 'Escape') emit('close')
}
onMounted(() => document.addEventListener('keydown', onKey))
onUnmounted(() => document.removeEventListener('keydown', onKey))
</script>

<template>
  <div class="overlay" @click.self="emit('close')">
    <form class="dialog" role="dialog" aria-modal="true" aria-label="Папка" @submit.prevent="save">
      <h2 class="heading">{{ folder ? 'Папка' : 'Новая папка' }}</h2>
      <input v-model="title" class="field" :maxlength="MAX_TITLE" placeholder="Название" />

      <label class="check"><input v-model="includePrivate" type="checkbox" /> Все личные чаты</label>
      <label class="check"><input v-model="includeGroups" type="checkbox" /> Все группы</label>
      <label class="check"><input v-model="onlyUnread" type="checkbox" /> Только непрочитанные</label>

      <p class="label">Чаты в папке</p>
      <ul class="chats">
        <li v-for="chat in chats.list" :key="chat.chatId">
          <label class="check">
            <input type="checkbox" :checked="chatIds.has(chat.chatId)" @change="toggle(chat.chatId)" />
            {{ chatTitle(chat) }}
          </label>
        </li>
      </ul>

      <div class="buttons">
        <button v-if="folder" type="button" class="button danger" @click="remove">Удалить папку</button>
        <button type="button" class="button" @click="emit('close')">Отмена</button>
        <button type="submit" class="button primary" :disabled="!canSave">Сохранить</button>
      </div>
    </form>
  </div>
</template>

<style scoped>
.overlay {
  position: fixed;
  inset: 0;
  z-index: 90;
  display: grid;
  place-items: center;
  padding: 16px;
  background: #000a;
}
.dialog {
  display: grid;
  gap: 8px;
  width: min(400px, 100%);
  max-height: min(640px, 92vh);
  padding: 18px;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--surface-solid);
}
.heading {
  margin: 0 0 4px;
  font-size: 16px;
}
.field {
  padding: 7px 10px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--bg);
}
.check {
  display: flex;
  align-items: center;
  gap: 8px;
}
.label {
  margin: 6px 0 0;
  color: var(--text-dim);
  font-size: 12px;
}
.chats {
  overflow-y: auto;
  max-height: 220px;
  margin: 0;
  padding: 0;
  list-style: none;
}
.chats li {
  padding: 3px 0;
}
.buttons {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 8px;
}
.button {
  padding: 7px 12px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text);
}
.button.primary {
  border-color: transparent;
  background: var(--accent);
  color: #04160b;
  font-weight: 600;
}
.button.primary:disabled {
  opacity: 0.5;
}
.button.danger {
  margin-right: auto;
  color: var(--danger);
}
</style>
