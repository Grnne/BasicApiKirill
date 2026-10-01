// A photo for an avatar (a user's or a group's): uploaded like any file, as kind `photo`.

import type { AttachmentDto, ClientConfigDto } from '@/shared/api/schema'
import * as mediaApi from './api'
import { formatSize, isPhotoType } from './lib'
import { measureVideo, putFile } from './transfer'
import { uploadFile } from './upload'

const deps = { createUpload: mediaApi.createUpload, completeUpload: mediaApi.completeUpload, putFile, measureVideo }

export const AVATAR_ACCEPT = 'image/jpeg,image/png,image/gif,image/webp'

/** Why the file cannot be an avatar, or null: only what the server decodes as a photo may. */
export function rejectAvatar(file: File, config: ClientConfigDto): string | null {
  if (!isPhotoType(file.type)) return 'Нужна фотография: JPEG, PNG, GIF или WebP'
  if (file.size > config.media.maxPhotoSize) return `Фото больше ${formatSize(config.media.maxPhotoSize)}`
  return null
}

export function uploadPhoto(file: File, signal: AbortSignal = new AbortController().signal): Promise<AttachmentDto> {
  return uploadFile(file, 'photo', deps, () => {}, signal)
}
