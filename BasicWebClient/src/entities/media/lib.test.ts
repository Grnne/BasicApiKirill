import { describe, expect, it } from 'vitest'

import { DEFAULT_CONFIG } from '@/entities/config/config.store'
import { albumConflict, formatDuration, formatSize, rejectFile, uploadKind } from './lib'

const MB = 1024 * 1024

describe('uploadKind', () => {
  it('pictures the server can decode are photos, unless larger than the photo limit', () => {
    expect(uploadKind({ type: 'image/png', size: MB }, DEFAULT_CONFIG)).toBe('photo')
    expect(uploadKind({ type: 'image/png', size: 25 * MB }, DEFAULT_CONFIG)).toBe('file')
    expect(uploadKind({ type: 'image/svg+xml', size: 10 }, DEFAULT_CONFIG)).toBe('file')
  })

  it('mp4, mov and webm are videos; anything else is a file', () => {
    expect(uploadKind({ type: 'video/mp4', size: MB }, DEFAULT_CONFIG)).toBe('video')
    expect(uploadKind({ type: 'application/pdf', size: MB }, DEFAULT_CONFIG)).toBe('file')
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
