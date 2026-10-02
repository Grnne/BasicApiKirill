import type { ClientConfigDto } from '@/shared/api/schema'

export type UploadKind = 'photo' | 'video' | 'file'

/** What the server decodes as a photo (and makes a preview of). */
const PHOTO_TYPES = new Set(['image/jpeg', 'image/png', 'image/gif', 'image/webp'])
const VIDEO_TYPES = new Set(['video/mp4', 'video/quicktime', 'video/webm'])

export const isPhotoType = (type: string) => PHOTO_TYPES.has(type)

/**
 * Width × height from the header of a PNG or GIF, or null for other types and headers that do not
 * parse. The server decodes these at full size for the preview, so a big one has to go as a file.
 */
export async function pngGifPixels(file: Blob): Promise<number | null> {
  if (file.type !== 'image/png' && file.type !== 'image/gif') return null
  try {
    const head = new DataView(await file.slice(0, 24).arrayBuffer())
    // PNG: the signature, then the IHDR chunk with width and height, big-endian.
    if (file.type === 'image/png')
      return head.byteLength >= 24 && head.getUint32(12) === 0x49484452 ? head.getUint32(16) * head.getUint32(20) : null
    // GIF: "GIF87a" or "GIF89a", then the screen width and height, little-endian.
    return head.byteLength >= 10 && head.getUint32(0) === 0x47494638 ? head.getUint16(6, true) * head.getUint16(8, true) : null
  } catch {
    return null
  }
}

export function uploadKind(
  file: { type: string; size: number; pixels?: number | null },
  config: ClientConfigDto,
): UploadKind {
  const decodable = file.pixels == null || file.pixels <= config.media.maxPngGifPhotoPixels
  if (isPhotoType(file.type) && file.size <= config.media.maxPhotoSize && decodable) return 'photo'
  if (VIDEO_TYPES.has(file.type)) return 'video'
  return 'file'
}

/** Why a file cannot be attached, or null. */
export function rejectFile(file: { size: number }, config: ClientConfigDto): string | null {
  if (file.size === 0) return 'Пустой файл'
  if (file.size > config.media.maxFileSize) return `Файл больше ${formatSize(config.media.maxFileSize)}`
  return null
}

/** An album is photos and videos, or files only (the server's rule, checked before upload). */
export function albumConflict(kinds: readonly UploadKind[]): boolean {
  const visual = kinds.some((k) => k === 'photo' || k === 'video')
  const files = kinds.some((k) => k === 'file')
  return visual && files
}

export function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} Б`
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} КБ`
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1).replace('.0', '')} МБ`
  return `${(bytes / 1024 / 1024 / 1024).toFixed(1)} ГБ`
}

export function formatDuration(ms: number): string {
  const total = Math.round(ms / 1000)
  const minutes = Math.floor(total / 60)
  return `${minutes}:${String(total % 60).padStart(2, '0')}`
}
