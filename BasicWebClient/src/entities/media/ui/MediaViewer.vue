<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'

import type { AttachmentDto } from '@/shared/api/schema'
import { useMediaLinksStore } from '../model/links.store'

const props = defineProps<{ items: AttachmentDto[]; start: number }>()
const emit = defineEmits<{ close: [] }>()

const links = useMediaLinksStore()
const index = ref(props.start)
const current = computed(() => props.items[index.value] ?? null)
const link = computed(() => (current.value ? links.get(current.value.id) : null))

function step(delta: number): void {
  const n = props.items.length
  index.value = (index.value + delta + n) % n
}

function onKey(event: KeyboardEvent): void {
  if (event.key === 'Escape') emit('close')
  else if (event.key === 'ArrowRight') step(1)
  else if (event.key === 'ArrowLeft') step(-1)
}
onMounted(() => document.addEventListener('keydown', onKey))
onUnmounted(() => document.removeEventListener('keydown', onKey))
</script>

<template>
  <div class="viewer" role="dialog" aria-modal="true" aria-label="Просмотр" @click.self="emit('close')">
    <template v-if="current">
      <video
        v-if="current.kind === 'video' && link?.url"
        class="media"
        :src="link.url"
        controls
        autoplay
      />
      <img v-else-if="link?.url || link?.thumbnailUrl" class="media" :src="link.url ?? link.thumbnailUrl ?? ''" :alt="current.fileName" />
      <p v-if="link && !link.url" class="expired">Оригинал больше не хранится — показано превью</p>
    </template>

    <button v-if="items.length > 1" type="button" class="nav prev" title="Предыдущее" @click="step(-1)">‹</button>
    <button v-if="items.length > 1" type="button" class="nav next" title="Следующее" @click="step(1)">›</button>
    <button type="button" class="close" title="Закрыть (Esc)" @click="emit('close')">✕</button>
    <span v-if="items.length > 1" class="counter">{{ index + 1 }} / {{ items.length }}</span>
  </div>
</template>

<style scoped>
.viewer {
  position: fixed;
  inset: 0;
  z-index: 95;
  display: grid;
  place-items: center;
  padding: 40px 56px;
  background: #000e;
}
.media {
  max-width: 100%;
  max-height: calc(100vh - 80px);
  object-fit: contain;
}
.expired {
  position: absolute;
  bottom: 16px;
  color: var(--text-dim);
  font-size: 12px;
}
.nav,
.close {
  position: absolute;
  border: none;
  background: transparent;
  color: #fff;
  font-size: 32px;
  opacity: 0.8;
}
.prev {
  left: 12px;
}
.next {
  right: 12px;
}
.close {
  top: 8px;
  right: 12px;
  font-size: 22px;
}
.counter {
  position: absolute;
  top: 14px;
  left: 16px;
  color: #ddd;
  font-size: 13px;
}
</style>
