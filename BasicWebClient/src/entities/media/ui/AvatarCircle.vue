<script setup lang="ts">
import { computed, ref, watch } from 'vue'

import { useMediaLinksStore } from '../model/links.store'

/** A user's or a group's photo; the initial when there is none or it does not load. */
const props = withDefaults(defineProps<{ avatarId: string | null; initial: string; size?: number }>(), { size: 34 })

const links = useMediaLinksStore()
const failed = ref(false)
watch(
  () => props.avatarId,
  () => (failed.value = false),
)

const src = computed(() => {
  if (!props.avatarId || failed.value) return null
  const link = links.get(props.avatarId)
  return link?.thumbnailUrl ?? link?.url ?? null
})
</script>

<template>
  <span class="avatar" :style="{ width: `${size}px`, height: `${size}px`, fontSize: `${Math.round(size * 0.42)}px` }">
    <img v-if="src" :src="src" alt="" @error="failed = true" />
    <template v-else>{{ initial }}</template>
  </span>
</template>

<style scoped>
.avatar {
  display: grid;
  flex-shrink: 0;
  place-items: center;
  overflow: hidden;
  border-radius: 50%;
  background: var(--surface-hover);
  color: var(--accent);
  font-weight: 700;
  line-height: 1;
}
img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}
</style>
