import { computed, ref, shallowRef, watch, type Ref } from 'vue'

import type { Message } from '@/entities/message/types'
import type { AttachmentDto } from '@/shared/api/schema'
import { describeError } from '@/shared/api/problem'
import { getChatMedia, type GalleryFilter } from '../api/media.api'

/** A file of the gallery with the message it came in. */
export interface GalleryFile {
  attachment: AttachmentDto
  message: Message
}

const KINDS: Record<Exclude<GalleryFilter, 'links'>, (a: AttachmentDto) => boolean> = {
  media: (a) => a.kind === 'photo' || a.kind === 'video',
  files: (a) => a.kind === 'file',
  voice: (a) => a.kind === 'voice',
}

/** The files of the tab's kind, newest first; a message of an album gives each of its files. */
export function galleryFiles(messages: readonly Message[], filter: GalleryFilter): GalleryFile[] {
  if (filter === 'links') return []
  const fits = KINDS[filter]
  return messages.flatMap((message) =>
    message.attachments.filter(fits).map((attachment) => ({ attachment, message })),
  )
}

/** One tab of the chat's gallery, paged back in time. A new chat or tab drops what is loading. */
export function useGallery(chatId: Readonly<Ref<string | null>>, filter: Readonly<Ref<GalleryFilter>>) {
  const messages = shallowRef<Message[]>([])
  const cursor = ref<string | null>(null)
  const done = ref(false)
  const busy = ref(false)
  const error = ref<string | null>(null)
  let controller: AbortController | null = null

  async function load(more: boolean): Promise<void> {
    const id = chatId.value
    if (!id || (more && (done.value || busy.value))) return

    controller?.abort()
    const current = (controller = new AbortController())
    busy.value = true
    error.value = null
    try {
      const page = await getChatMedia(id, filter.value, more ? cursor.value : null, current.signal)
      if (current.signal.aborted) return
      // A message may come twice when a new one shifted the pages.
      const known = new Set(more ? messages.value.map((m) => m.id) : [])
      const fresh = page.items.filter((m) => !known.has(m.id))
      messages.value = more ? [...messages.value, ...fresh] : page.items
      cursor.value = page.nextCursor
      done.value = page.nextCursor === null
    } catch (e) {
      if (!current.signal.aborted) error.value = describeError(e)
    } finally {
      if (!current.signal.aborted) busy.value = false
    }
  }

  watch(
    [chatId, filter],
    () => {
      controller?.abort()
      busy.value = false
      messages.value = []
      cursor.value = null
      done.value = false
      void load(false)
    },
    { immediate: true },
  )

  const files = computed(() => galleryFiles(messages.value, filter.value))

  function stop(): void {
    controller?.abort()
  }

  // Again what failed: the next page if some are loaded, else the first one.
  const retry = () => load(messages.value.length > 0)

  return { messages, files, done, busy, error, loadMore: () => load(true), retry, stop }
}
