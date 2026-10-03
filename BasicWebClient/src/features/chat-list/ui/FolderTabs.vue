<script setup lang="ts">
import { ref } from 'vue'

import { useChatsStore } from '@/entities/chat/model/chats.store'
import type { FolderDto } from '@/shared/api/schema'
import { useChatListStore } from '../model/chat-list.store'
import FolderEditor from './FolderEditor.vue'

const store = useChatListStore()
const chats = useChatsStore()

/** undefined — closed; null — a new folder. */
const editing = ref<FolderDto | null | undefined>(undefined)

/* Tabs are reordered by dragging one onto another. */
const dragged = ref<string | null>(null)

function onDrop(target: FolderDto): void {
  const moving = dragged.value
  dragged.value = null
  if (!moving || moving === target.id) return
  const ids = chats.folders.map((f) => f.id).filter((id) => id !== moving)
  ids.splice(ids.indexOf(target.id), 0, moving)
  void store.reorderFolders(ids)
}
</script>

<template>
  <div v-if="chats.loaded" class="row">
    <nav class="tabs" aria-label="Папки">
      <button
        type="button"
        :class="['tab', { active: store.selectedFolderId === null }]"
        @click="store.selectedFolderId = null"
      >
        Все
      </button>
      <button
        v-for="folder in chats.folders"
        :key="folder.id"
        type="button"
        draggable="true"
        :class="['tab', { active: store.selectedFolderId === folder.id }]"
        title="Правый клик — изменить"
        @click="store.selectedFolderId = folder.id"
        @contextmenu.prevent="editing = folder"
        @dragstart="dragged = folder.id"
        @dragover.prevent
        @drop.prevent="onDrop(folder)"
        @dragend="dragged = null"
      >
        {{ folder.title }}
      </button>
      <button type="button" class="tab add" title="Новая папка" aria-label="Новая папка" @click="editing = null">+</button>
    </nav>

    <FolderEditor v-if="editing !== undefined" :folder="editing" @close="editing = undefined" />
  </div>
</template>

<style scoped>
.row {
  display: flex;
  align-items: flex-end;
  border-bottom: 1px solid var(--border);
}
.tabs {
  display: flex;
  flex: 1;
  gap: 2px;
  min-width: 0;
  overflow-x: auto;
  padding: 6px 8px 0;
}
.tab {
  flex: none;
  padding: 5px 10px;
  border: none;
  border-bottom: 2px solid transparent;
  background: transparent;
  color: var(--text-dim);
  font-size: 13px;
  white-space: nowrap;
}
.tab.active {
  border-bottom-color: var(--accent);
  color: var(--text);
}
.tab.add {
  color: var(--text-faint);
}
</style>
