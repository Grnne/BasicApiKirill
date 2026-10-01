import { flushPromises } from '@vue/test-utils'
import { effectScope, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { AttachmentDto } from '@/shared/api/schema'
import { message } from '@/testing/fixtures'
import * as mediaApi from '../api/media.api'
import type { GalleryFilter } from '../api/media.api'
import { galleryFiles, useGallery } from './useGallery'

vi.mock('../api/media.api', () => ({ getChatMedia: vi.fn() }))

const file = (id: string, kind: string): AttachmentDto => ({
  id, kind, fileName: id, mimeType: 'x/y', size: 1, width: null, height: null, durationMs: null,
  waveform: null, hasThumbnail: false, state: 'stored',
})

const page = (ids: string[], nextCursor: string | null) => ({
  items: ids.map((id) => message({ id, attachments: [file(`${id}-a`, 'photo')] })),
  nextCursor,
  hasMore: nextCursor !== null,
})

function run(filter = ref<GalleryFilter>('media'), chatId = ref<string | null>('c1')) {
  const scope = effectScope()
  const gallery = scope.run(() => useGallery(chatId, filter))!
  return { gallery, filter, chatId, stop: () => scope.stop() }
}

beforeEach(() => vi.mocked(mediaApi.getChatMedia).mockReset())

describe('gallery', () => {
  it('an album gives each of its files; only the tab kind is shown', () => {
    const album = message({ id: 'm', attachments: [file('p', 'photo'), file('v', 'video'), file('d', 'file')] })
    expect(galleryFiles([album], 'media').map((f) => f.attachment.id)).toEqual(['p', 'v'])
    expect(galleryFiles([album], 'files').map((f) => f.attachment.id)).toEqual(['d'])
    expect(galleryFiles([album], 'links')).toEqual([])
  })

  it('"retry" after a page that failed loads that page, keeping the ones before', async () => {
    // The bug: it reloaded from the first page, and five pages scrolled through were gone.
    vi.mocked(mediaApi.getChatMedia).mockResolvedValueOnce(page(['m1'], 'c-2'))
    const { gallery } = run()
    await flushPromises()
    vi.mocked(mediaApi.getChatMedia).mockRejectedValueOnce(new Error('offline'))
    await gallery.loadMore()
    expect(gallery.error.value).not.toBeNull()

    vi.mocked(mediaApi.getChatMedia).mockResolvedValueOnce(page(['m2'], null))
    await gallery.retry()

    expect(vi.mocked(mediaApi.getChatMedia).mock.calls.at(-1)![2]).toBe('c-2')
    expect(gallery.messages.value.map((m) => m.id)).toEqual(['m1', 'm2'])
  })

  it('pages go back in time; a message shifted onto the next page is not shown twice', async () => {
    vi.mocked(mediaApi.getChatMedia)
      .mockResolvedValueOnce(page(['m3', 'm2'], 'cur'))
      .mockResolvedValueOnce(page(['m2', 'm1'], null))
    const { gallery, stop } = run()
    await flushPromises()

    await gallery.loadMore()
    expect(mediaApi.getChatMedia).toHaveBeenLastCalledWith('c1', 'media', 'cur', expect.any(AbortSignal))
    expect(gallery.messages.value.map((m) => m.id)).toEqual(['m3', 'm2', 'm1'])
    expect(gallery.done.value).toBe(true)

    await gallery.loadMore()
    expect(mediaApi.getChatMedia).toHaveBeenCalledTimes(2)
    stop()
  })

  it('another tab drops the answer of the previous one', async () => {
    let answer!: (p: ReturnType<typeof page>) => void
    vi.mocked(mediaApi.getChatMedia)
      .mockImplementationOnce(() => new Promise((resolve) => (answer = resolve)))
      .mockResolvedValueOnce(page(['f1'], null))
    const { gallery, filter, stop } = run()

    filter.value = 'files'
    await flushPromises()
    answer(page(['old'], null))
    await flushPromises()

    expect(gallery.messages.value.map((m) => m.id)).toEqual(['f1'])
    expect(gallery.busy.value).toBe(false)
    stop()
  })
})
