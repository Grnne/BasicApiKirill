<script setup lang="ts">
import { computed, onUnmounted, ref, toRef } from 'vue'

import { chatInitial, chatTitle } from '@/entities/chat/lib'
import { useChatDetailsStore } from '@/entities/chat/model/details.store'
import type { ChatListItem } from '@/entities/chat/types'
import { formatDuration } from '@/entities/media/lib'
import { useMediaLinksStore } from '@/entities/media/model/links.store'
import AttachmentList from '@/entities/media/ui/AttachmentList.vue'
import AvatarCircle from '@/entities/media/ui/AvatarCircle.vue'
import MediaViewer from '@/entities/media/ui/MediaViewer.vue'
import FormattedText from '@/entities/message/ui/FormattedText'
import { formatDay } from '@/shared/lib/date'
import { plural } from '@/shared/lib/plural'
import type { GalleryFilter } from '../api/media.api'
import { useGallery } from '../model/useGallery'

const props = defineProps<{ chat: ChatListItem; meId: string | null }>()
const emit = defineEmits<{ close: []; jump: [messageId: string] }>()

const details = useChatDetailsStore()
const links = useMediaLinksStore()

const TABS: { filter: GalleryFilter; label: string; empty: string }[] = [
  { filter: 'media', label: 'Медиа', empty: 'Фото и видео пока нет' },
  { filter: 'files', label: 'Файлы', empty: 'Файлов пока нет' },
  { filter: 'voice', label: 'Голосовые', empty: 'Голосовых пока нет' },
  { filter: 'links', label: 'Ссылки', empty: 'Ссылок пока нет' },
]
const filter = ref<GalleryFilter>('media')
const tab = computed(() => TABS.find((t) => t.filter === filter.value)!)

const gallery = useGallery(toRef(() => props.chat.chatId), filter)
onUnmounted(() => gallery.stop())

const subtitle = computed(() => {
  if (props.chat.type === 'private') return props.chat.companionUsername ? `@${props.chat.companionUsername}` : ''
  if (props.chat.type !== 'group') return ''
  const count = details.get(props.chat.chatId)?.participants.length
  return count ? `${count} ${plural(count, ['участник', 'участника', 'участников'])}` : ''
})

const empty = computed(() =>
  filter.value === 'links' ? gallery.messages.value.length === 0 : gallery.files.value.length === 0,
)

/** Index in the media tab's files open in the viewer, or null. */
const viewing = ref<number | null>(null)
const visual = computed(() => gallery.files.value.map((f) => f.attachment))

function jumpFromViewer(index: number): void {
  const file = gallery.files.value[index]
  viewing.value = null
  if (file) emit('jump', file.message.id)
}

function onScroll(event: Event): void {
  const el = event.target as HTMLElement
  if (el.scrollHeight - el.scrollTop - el.clientHeight < 200) void gallery.loadMore()
}
</script>

<template>
  <aside class="info" aria-label="О чате">
    <header class="head">
      <span class="heading">О чате</span>
      <button type="button" class="close" title="Закрыть" @click="emit('close')">✕</button>
    </header>

    <div class="card">
      <AvatarCircle :avatar-id="chat.avatarId" :initial="chatInitial(chat)" :size="72" />
      <span class="title">{{ chatTitle(chat) }}</span>
      <span v-if="subtitle" class="subtitle">{{ subtitle }}</span>
    </div>

    <nav class="tabs" role="tablist">
      <button
        v-for="t in TABS"
        :key="t.filter"
        type="button"
        role="tab"
        :aria-selected="filter === t.filter"
        :class="['tab', { active: filter === t.filter }]"
        @click="filter = t.filter"
      >
        {{ t.label }}
      </button>
    </nav>

    <div class="content" @scroll.passive="onScroll">
      <div v-if="filter === 'media'" class="grid">
        <button
          v-for="(f, index) in gallery.files.value"
          :key="f.attachment.id"
          type="button"
          class="tile"
          :title="f.attachment.fileName"
          @click="viewing = index"
        >
          <img
            v-if="f.attachment.hasThumbnail && links.get(f.attachment.id)?.thumbnailUrl"
            :src="links.get(f.attachment.id)!.thumbnailUrl!"
            alt=""
            loading="lazy"
          />
          <span v-else class="placeholder">{{ f.attachment.kind === 'video' ? '🎬' : '🖼' }}</span>
          <span v-if="f.attachment.kind === 'video' && f.attachment.durationMs" class="duration">
            {{ formatDuration(f.attachment.durationMs) }}
          </span>
        </button>
      </div>

      <template v-else-if="filter === 'links'">
        <div v-for="m in gallery.messages.value" :key="m.id" class="row">
          <p class="link-text"><FormattedText :text="m.text" :entities="m.entities" :me-id="meId" /></p>
          <span class="meta">
            {{ m.senderName }} · {{ formatDay(m.createdAt) }}
            <button type="button" class="jump" @click="emit('jump', m.id)">в чате</button>
          </span>
        </div>
      </template>

      <template v-else>
        <div v-for="f in gallery.files.value" :key="f.attachment.id" class="row">
          <AttachmentList :attachments="[f.attachment]" />
          <span class="meta">
            {{ f.message.senderName }} · {{ formatDay(f.message.createdAt) }}
            <button type="button" class="jump" @click="emit('jump', f.message.id)">в чате</button>
          </span>
        </div>
      </template>

      <p v-if="gallery.error.value" class="note error">
        {{ gallery.error.value }}
        <button type="button" class="jump" @click="gallery.reload()">Повторить</button>
      </p>
      <p v-else-if="gallery.busy.value" class="note">загрузка…</p>
      <p v-else-if="empty" class="note">{{ tab.empty }}</p>
      <button
        v-else-if="!gallery.done.value"
        type="button"
        class="more"
        @click="gallery.loadMore()"
      >
        Ещё
      </button>
    </div>

    <MediaViewer
      v-if="viewing !== null"
      :items="visual"
      :start="viewing"
      jumpable
      @close="viewing = null"
      @jump="jumpFromViewer"
    />
  </aside>
</template>

<style scoped>
.info {
  display: flex;
  flex-direction: column;
  overflow: hidden;
  border-left: 1px solid var(--border);
  background: var(--surface-solid);
}
.head {
  display: flex;
  align-items: center;
  padding: 12px 16px;
  border-bottom: 1px solid var(--border);
}
.heading {
  font-weight: 600;
}
.close {
  margin-left: auto;
  border: none;
  background: none;
  color: var(--text-dim);
}
.card {
  display: grid;
  justify-items: center;
  gap: 4px;
  padding: 16px;
}
.title {
  font-size: 16px;
  font-weight: 600;
  text-align: center;
  overflow-wrap: anywhere;
}
.subtitle {
  color: var(--text-dim);
  font-size: 12px;
}
.tabs {
  display: flex;
  border-bottom: 1px solid var(--border);
}
.tab {
  flex: 1;
  padding: 8px 4px;
  border: none;
  border-bottom: 2px solid transparent;
  background: none;
  color: var(--text-dim);
  font-size: 12px;
}
.tab.active {
  border-bottom-color: var(--accent);
  color: var(--text);
}
.content {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
}
.grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 2px;
}
.tile {
  position: relative;
  aspect-ratio: 1;
  overflow: hidden;
  padding: 0;
  border: none;
  background: var(--surface-hover);
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
  font-size: 24px;
}
.duration {
  position: absolute;
  right: 3px;
  bottom: 3px;
  padding: 0 4px;
  border-radius: 4px;
  background: #000a;
  color: #fff;
  font-size: 10px;
}
.row {
  display: grid;
  gap: 2px;
  padding: 8px 14px;
  border-bottom: 1px solid var(--border);
}
.row :deep(.attachments) {
  margin-bottom: 0;
}
.link-text {
  margin: 0;
  overflow-wrap: anywhere;
}
.meta {
  color: var(--text-faint);
  font-size: 11px;
}
.jump {
  margin-left: 6px;
  padding: 0;
  border: none;
  background: none;
  color: var(--accent);
  font-size: 11px;
}
.note {
  margin: 0;
  padding: 12px 16px;
  color: var(--text-dim);
  font-size: 12px;
  text-align: center;
}
.note.error {
  color: var(--danger);
}
.more {
  width: 100%;
  padding: 8px;
  border: none;
  background: none;
  color: var(--accent);
}
</style>
