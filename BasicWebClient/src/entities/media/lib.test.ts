import { describe, expect, it } from 'vitest'

import { DEFAULT_CONFIG } from '@/entities/config/config.store'
import { albumConflict, formatDuration, formatSize, pngGifPixels, rejectFile, uploadKind } from './lib'

const MB = 1024 * 1024

describe('uploadKind', () => {
  it('pictures the server can decode are photos, unless larger than the photo limit', () => {
    expect(uploadKind({ type: 'image/png', size: MB }, DEFAULT_CONFIG)).toBe('photo')
    expect(uploadKind({ type: 'image/png', size: 25 * MB }, DEFAULT_CONFIG)).toBe('file')
    expect(uploadKind({ type: 'image/svg+xml', size: 10 }, DEFAULT_CONFIG)).toBe('file')
  })

  it('a PNG or GIF with more pixels than the server decodes is a file', () => {
    const limit = DEFAULT_CONFIG.media.maxPngGifPhotoPixels
    expect(uploadKind({ type: 'image/png', size: MB, pixels: limit }, DEFAULT_CONFIG)).toBe('photo')
    expect(uploadKind({ type: 'image/png', size: MB, pixels: limit + 1 }, DEFAULT_CONFIG)).toBe('file')
    expect(uploadKind({ type: 'image/png', size: MB, pixels: null }, DEFAULT_CONFIG)).toBe('photo')
  })

  it('mp4, mov and webm are videos; anything else is a file', () => {
    expect(uploadKind({ type: 'video/mp4', size: MB }, DEFAULT_CONFIG)).toBe('video')
    expect(uploadKind({ type: 'application/pdf', size: MB }, DEFAULT_CONFIG)).toBe('file')
  })
})

/** The first bytes of a PNG: signature and IHDR with the given size. */
function pngHeader(width: number, height: number): Uint8Array<ArrayBuffer> {
  const head = new Uint8Array(24)
  head.set([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52])
  new DataView(head.buffer).setUint32(16, width)
  new DataView(head.buffer).setUint32(20, height)
  return head
}

describe('pngGifPixels', () => {
  it('reads the size from a PNG or GIF header', async () => {
    expect(await pngGifPixels(new Blob([pngHeader(4000, 4000)], { type: 'image/png' }))).toBe(16_000_000)
    const gif = new Uint8Array([0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x10, 0x00, 0x20, 0x00])
    expect(await pngGifPixels(new Blob([gif], { type: 'image/gif' }))).toBe(16 * 32)
  })

  it('other types and headers that do not parse give null', async () => {
    expect(await pngGifPixels(new Blob(['abc'], { type: 'image/png' }))).toBeNull()
    expect(await pngGifPixels(new Blob([pngHeader(10, 10)], { type: 'image/jpeg' }))).toBeNull()
  })
})

describe('limits and albums', () => {
  it('empty and too large files are refused before upload', () => {
    expect(rejectFile({ size: 0 }, DEFAULT_CONFIG)).toBe('Пустой файл')
    expect(rejectFile({ size: 101 * MB }, DEFAULT_CONFIG)).toBe('Файл больше 100 МБ')
    expect(rejectFile({ size: MB }, DEFAULT_CONFIG)).toBeNull()
  })

  it('photos and videos go together, files only with files', () => {
    expect(albumConflict(['photo', 'video'])).toBe(false)
    expect(albumConflict(['file', 'file'])).toBe(false)
    expect(albumConflict(['photo', 'file'])).toBe(true)
  })

  it('sizes and durations read like people write them', () => {
    expect(formatSize(512)).toBe('512 Б')
    expect(formatSize(1536)).toBe('2 КБ')
    expect(formatSize(3 * MB)).toBe('3 МБ')
    expect(formatSize(1.5 * MB)).toBe('1.5 МБ')
    expect(formatDuration(65_000)).toBe('1:05')
  })
})
