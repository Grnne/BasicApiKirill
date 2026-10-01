import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { defineComponent } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useConfigStore, DEFAULT_CONFIG } from '@/entities/config/config.store'
import * as mediaApi from '@/entities/media/api'
import { useNoticesStore } from '@/shared/ui/notices.store'
import { useUploads } from './useUploads'

vi.mock('@/entities/media/api', () => ({
  createUpload: vi.fn(async () => ({
    attachmentId: 'att', uploadUrl: 'https://s/o', contentType: 'image/png', expiresAt: '', thumbnailUploadUrl: null, thumbnailContentType: null,
  })),
  completeUpload: vi.fn(async (id: string) => ({
    id, kind: 'photo', fileName: 'a.png', mimeType: 'image/png', size: 3, width: 1, height: 1,
    durationMs: null, waveform: null, hasThumbnail: true, state: 'stored',
  })),
}))
vi.mock('@/entities/media/transfer', () => ({ putFile: vi.fn(async () => {}), measureVideo: vi.fn() }))

function setup() {
  const pinia = createPinia()
  setActivePinia(pinia)
  useConfigStore().config = { ...DEFAULT_CONFIG, media: { ...DEFAULT_CONFIG.media, enabled: true } }
  let api!: ReturnType<typeof useUploads>
  mount(defineComponent({ setup: () => { api = useUploads(); return () => null } }), { global: { plugins: [pinia] } })
  return api
}

const png = (name = 'a.png') => new File(['abc'], name, { type: 'image/png' })
const pdf = () => new File(['%PDF'], 'doc.pdf', { type: 'application/pdf' })

beforeEach(() => {
  globalThis.URL.createObjectURL = vi.fn(() => 'blob:preview')
  globalThis.URL.revokeObjectURL = vi.fn()
  vi.mocked(mediaApi.createUpload).mockClear()
})

describe('useUploads', () => {
  it('uploads a chosen file at once; ready with its attachment when done', async () => {
    const uploads = setup()

    uploads.add([png()])
    expect(uploads.busy.value).toBe(true)
    await flushPromises()

    expect(uploads.ready.value).toBe(true)
    expect(uploads.attachments.value.map((a) => a.id)).toEqual(['att'])
  })

  it('a file that cannot go into the same album is refused with a reason', () => {
    const uploads = setup()

    uploads.add([png(), pdf()])

    expect(uploads.items.value.map((i) => i.kind)).toEqual(['photo'])
    expect(useNoticesStore().items[0]!.text).toContain('отдельно')
  })

  it('no more files than the server allows in one message', () => {
    const uploads = setup()

    uploads.add(Array.from({ length: 11 }, (_, i) => png(`${i}.png`)))

    expect(uploads.items.value).toHaveLength(10)
    expect(useNoticesStore().items[0]!.text).toContain('не больше 10')
  })

  it('without file storage nothing is uploaded', () => {
    const uploads = setup()
    useConfigStore().config = DEFAULT_CONFIG

    uploads.add([png()])

    expect(uploads.items.value).toEqual([])
    expect(mediaApi.createUpload).not.toHaveBeenCalled()
  })
})
