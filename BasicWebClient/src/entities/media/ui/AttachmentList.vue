<script setup lang="ts">
import { computed, ref } from 'vue'

import type { AttachmentDto } from '@/shared/api/schema'
import { formatDuration, formatSize } from '../lib'
import { useMediaLinksStore } from '../model/links.store'
import MediaViewer from './MediaViewer.vue'

const props = defineProps<{ attachments: AttachmentDto[] }>()

const links = useMediaLinksStore()

const visual = computed(() => props.attachments.filter((a) => a.kind === 'photo' || a.kind === 'video'))
const others = computed(() => props.attachments.filter((a) => a.kind !== 'photo' && a.kind !== 'video'))

/** Index in `visual` open in the viewer, or null. */
const viewing = ref<number | null>(null)

const expired = (a: AttachmentDto) => a.state === 'expired'

/** One picture keeps its proportions; an album is a grid of squares. */
function boxStyle(a: AttachmentDto): Record<string, string> {
  if (visual.value.length > 1 || !a.width || !a.height) return {}
  return { aspectRatio: `${a.width} / ${a.height}` }
}
</script>

<template>
  <div class="attachments">
    <div v-if="visual.length > 0" :class="['grid', { album: visual.length > 1 }]">
      <button
        v-for="(a, index) in visual"
        :key="a.id"
        type="button"
        class="tile"
        :style="boxStyle(a)"
        :title="a.fileName"
        @click="viewing = index"
      >
        <img v-if="a.hasThumbnail && links.get(a.id)?.thumbnailUrl" :src="links.get(a.id)!.thumbnailUrl!" alt="" loading="lazy" />
        <span v-else class="placeholder">{{ a.kind === 'video' ? '🎬' : '🖼' }}</span>
        <span v-if="a.kind === 'video'" class="play">▶</span>
        <span v-if="a.kind === 'video' && a.durationMs" class="duration">{{ formatDuration(a.durationMs) }}</span>
      </button>
    </div>

    <template v-for="a in others" :key="a.id">
      <div v-if="a.kind === 'voice'" class="voice">
        <audio v-if="!expired(a) && links.get(a.id)?.url" :src="links.get(a.id)!.url!" controls preload="none" />
        <span v-else class="gone">🎤 Голосовое больше не хранится</span>
      </div>
      <div v-else class="file">
        <span class="icon">📄</span>
        <span class="info">
          <a
            v-if="!expired(a) && links.get(a.id)?.url"
            class="name"
            :href="links.get(a.id)!.url!"
            :download="a.fileName"
            target="_blank"
            rel="noopener noreferrer"
          >{{ a.fileName }}</a>
          <span v-else class="name">{{ a.fileName }}</span>
          <span class="size">{{ expired(a) ? 'Файл больше не хранится' : formatSize(a.size) }}</span>
        </span>
      </div>
    </template>

    <MediaViewer v-if="viewing !== null" :items="visual" :start="viewing" @close="viewing = null" />
  </div>
</template>

<style scoped>
.attachments {
  display: grid;
  gap: 4px;
  margin-bottom: 4px;
}
.grid {
  display: grid;
  gap: 2px;
  max-width: 360px;
}
.grid.album {
  grid-template-columns: repeat(2, 1fr);
}
.tile {
  position: relative;
  overflow: hidden;
  min-height: 80px;
  padding: 0;
  border: none;
  border-radius: 6px;
  background: var(--surface-hover);
}
.grid.album .tile {
  aspect-ratio: 1;
}
.tile img {
  display: block;
  width: 100%;
  height: 100%;
  object-fit: cover;
}
.placeholder {
  display: grid;
  place-items: center;
  height: 100%;
  min-height: 80px;
  font-size: 28px;
}
.play {
  position: absolute;
  top: 50%;
  left: 50%;
  display: grid;
  place-items: center;
  width: 40px;
  height: 40px;
  border-radius: 50%;
  background: #000a;
  color: #fff;
  transform: translate(-50%, -50%);
}
.duration {
  position: absolute;
  right: 4px;
  bottom: 4px;
  padding: 0 5px;
  border-radius: 4px;
  background: #000a;
  color: #fff;
  font-size: 11px;
}
.file {
  display: flex;
  align-items: center;
  gap: 8px;
}
.icon {
  font-size: 24px;
}
.info {
  display: grid;
  min-width: 0;
}
.name {
  overflow: hidden;
  color: var(--accent);
  text-overflow: ellipsis;
  white-space: nowrap;
}
span.name {
  color: var(--text);
}
.size,
.gone {
  color: var(--text-faint);
  font-size: 11px;
}
.voice audio {
  max-width: 260px;
  height: 32px;
}
</style>
