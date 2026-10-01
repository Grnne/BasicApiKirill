<script setup lang="ts">
import { formatSize } from '@/entities/media/lib'
import type { UploadItem } from '../lib/useUploads'

defineProps<{ items: UploadItem[] }>()
defineEmits<{ remove: [key: number] }>()

const icons = { photo: '🖼', video: '🎬', file: '📄' } as const
</script>

<template>
  <ul class="tray">
    <li v-for="item in items" :key="item.key" :class="['item', item.state]">
      <img v-if="item.preview" class="thumb" :src="item.preview" alt="" />
      <span v-else class="icon">{{ icons[item.kind] }}</span>
      <span class="info">
        <span class="name">{{ item.file.name }}</span>
        <span class="size">
          <template v-if="item.state === 'failed'">{{ item.error }}</template>
          <template v-else>{{ formatSize(item.file.size) }}</template>
        </span>
        <span v-if="item.state === 'uploading'" class="bar"><span :style="{ width: `${Math.round(item.progress * 100)}%` }" /></span>
      </span>
      <button type="button" class="remove" :title="item.state === 'uploading' ? 'Отменить' : 'Убрать'"
        :aria-label="`${item.state === 'uploading' ? 'Отменить' : 'Убрать'}: ${item.file.name}`" @click="$emit('remove', item.key)">
        ✕
      </button>
    </li>
  </ul>
</template>

<style scoped>
.tray {
  grid-column: 1 / -1;
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin: 0;
  padding: 0;
  list-style: none;
}
.item {
  display: flex;
  align-items: center;
  gap: 6px;
  max-width: 240px;
  padding: 4px 6px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--bg);
}
.item.failed {
  border-color: var(--danger);
}
.thumb {
  width: 40px;
  height: 40px;
  border-radius: 4px;
  object-fit: cover;
}
.icon {
  font-size: 22px;
}
.info {
  display: grid;
  min-width: 0;
}
.name {
  overflow: hidden;
  font-size: 12px;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.size {
  color: var(--text-faint);
  font-size: 11px;
}
.item.failed .size {
  color: var(--danger);
}
.bar {
  height: 3px;
  margin-top: 2px;
  border-radius: 2px;
  background: var(--surface-hover);
}
.bar span {
  display: block;
  height: 100%;
  border-radius: 2px;
  background: var(--accent);
}
.remove {
  border: none;
  background: none;
  color: var(--text-dim);
}
</style>
