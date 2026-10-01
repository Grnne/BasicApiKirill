// Files chosen for the next message: each uploads at once, the message waits for all of them.

import { computed, onUnmounted, ref } from 'vue'

import { useConfigStore } from '@/entities/config/config.store'
import * as mediaApi from '@/entities/media/api'
import { albumConflict, rejectFile, uploadKind, type UploadKind } from '@/entities/media/lib'
import { measureVideo, putFile } from '@/entities/media/transfer'
import { uploadFile } from '@/entities/media/upload'
import { describeError } from '@/shared/api/problem'
import type { AttachmentDto } from '@/shared/api/schema'
import { useNoticesStore } from '@/shared/ui/notices.store'

export interface UploadItem {
  key: number
  file: File
  kind: UploadKind
  /** blob: URL of a picture, for the tray only; revoked when the item goes. */
  preview: string | null
  progress: number
  state: 'uploading' | 'done' | 'failed'
  error: string | null
  attachment: AttachmentDto | null
  controller: AbortController
}

const deps = {
  createUpload: mediaApi.createUpload,
  completeUpload: mediaApi.completeUpload,
  putFile,
  measureVideo,
}

export function useUploads() {
  const config = useConfigStore()
  const notices = useNoticesStore()
  const items = ref<UploadItem[]>([])
  let nextKey = 1

  const busy = computed(() => items.value.some((i) => i.state === 'uploading'))
  const ready = computed(() => items.value.length > 0 && items.value.every((i) => i.state === 'done'))
  const attachments = computed(() => items.value.map((i) => i.attachment).filter((a): a is AttachmentDto => !!a))

  function patch(key: number, change: Partial<UploadItem>): void {
    const item = items.value.find((i) => i.key === key)
    if (item) Object.assign(item, change)
  }

  async function start(item: UploadItem): Promise<void> {
    try {
      const attachment = await uploadFile(item.file, item.kind, deps, (p) => patch(item.key, { progress: p }), item.controller.signal)
      patch(item.key, { state: 'done', attachment, progress: 1 })
    } catch (e) {
      if (item.controller.signal.aborted) return
      patch(item.key, { state: 'failed', error: describeError(e) })
    }
  }

  function add(files: Iterable<File>): void {
    if (!config.config.media.enabled) {
      notices.push('Файлы на этом сервере отключены')
      return
    }
    for (const file of files) {
      const problem = rejectFile(file, config.config)
      if (problem) {
        notices.push(`${file.name}: ${problem}`)
        continue
      }
      if (items.value.length >= config.config.messages.maxAttachments) {
        notices.push(`В одном сообщении — не больше ${config.config.messages.maxAttachments} файлов`)
        return
      }
      const kind = uploadKind(file, config.config)
      if (albumConflict([...items.value.map((i) => i.kind), kind])) {
        notices.push('Фото и видео отправляются отдельно от других файлов')
        continue
      }
      const item: UploadItem = {
        key: nextKey++,
        file,
        kind,
        preview: kind === 'photo' ? URL.createObjectURL(file) : null,
        progress: 0,
        state: 'uploading',
        error: null,
        attachment: null,
        controller: new AbortController(),
      }
      items.value.push(item)
      void start(items.value.at(-1)!)
    }
  }

  function remove(key: number): void {
    const item = items.value.find((i) => i.key === key)
    if (!item) return
    item.controller.abort()
    if (item.preview) URL.revokeObjectURL(item.preview)
    items.value = items.value.filter((i) => i.key !== key)
  }

  /** After sending: the files belong to the message now. */
  function clear(): void {
    for (const item of items.value) if (item.preview) URL.revokeObjectURL(item.preview)
    items.value = []
  }

  onUnmounted(() => {
    for (const item of items.value) item.controller.abort()
    clear()
  })

  return { items, busy, ready, attachments, add, remove, clear }
}
