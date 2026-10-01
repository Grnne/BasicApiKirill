import { describe, expect, it, vi } from 'vitest'

import type { AttachmentDto } from '@/shared/api/schema'
import { uploadFile, type UploadDeps } from './upload'

const attachment = (id: string): AttachmentDto => ({
  id, kind: 'photo', fileName: 'a.png', mimeType: 'image/png', size: 3, width: 1, height: 1,
  durationMs: null, waveform: null, hasThumbnail: true, state: 'stored',
})

function deps(thumbnail: Blob | null = null) {
  const calls: string[] = []
  const d = {
    createUpload: vi.fn(async (body) => {
      calls.push(`create ${body.kind}`)
      return {
        attachmentId: 'att-1', uploadUrl: 'https://s/o/1', contentType: 'image/png', expiresAt: '',
        thumbnailUploadUrl: thumbnail ? 'https://s/o/1t' : null, thumbnailContentType: thumbnail ? 'image/jpeg' : null,
      }
    }),
    putFile: vi.fn(async (url: string, contentType: string) => {
      calls.push(`put ${url} ${contentType}`)
    }),
    completeUpload: vi.fn(async (id: string) => {
      calls.push(`complete ${id}`)
      return attachment(id)
    }),
    measureVideo: vi.fn(async () => ({ width: 640, height: 360, durationMs: 5000, thumbnail })),
  } satisfies UploadDeps
  return { d, calls }
}

describe('uploadFile', () => {
  it('ticket, PUT with the content type from the ticket, complete', async () => {
    const { d, calls } = deps()
    const file = new File(['abc'], 'a.png', { type: 'image/png' })

    const result = await uploadFile(file, 'photo', d, () => {}, new AbortController().signal)

    expect(calls).toEqual(['create photo', 'put https://s/o/1 image/png', 'complete att-1'])
    expect(d.createUpload).toHaveBeenCalledWith(
      { kind: 'photo', fileName: 'a.png', mimeType: 'image/png', size: 3 }, expect.any(AbortSignal))
    expect(result.id).toBe('att-1')
  })

  it('a video is measured and its frame goes as the preview', async () => {
    const { d, calls } = deps(new Blob(['jpg'], { type: 'image/jpeg' }))
    const file = new File(['vid'], 'v.mp4', { type: 'video/mp4' })

    await uploadFile(file, 'video', d, () => {}, new AbortController().signal)

    expect(d.createUpload).toHaveBeenCalledWith(
      expect.objectContaining({ width: 640, height: 360, durationMs: 5000, withThumbnail: true }), expect.any(AbortSignal))
    expect(calls).toContain('put https://s/o/1t image/jpeg')
  })
})
