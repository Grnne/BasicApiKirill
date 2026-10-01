import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { expect, it, vi } from 'vitest'

import type { AttachmentDto } from '@/shared/api/schema'
import AttachmentList from './AttachmentList.vue'

vi.mock('../api', () => ({
  getLinks: vi.fn(async (ids: string[]) => ({
    items: ids.map((id) => ({
      attachmentId: id,
      url: id === 'gone' ? null : `https://s/${id}`,
      thumbnailUrl: `https://s/${id}.t`,
      expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
    })),
  })),
}))

const file = (id: string, extra: Partial<AttachmentDto> = {}): AttachmentDto => ({
  id, kind: 'file', fileName: `${id}.pdf`, mimeType: 'application/octet-stream', size: 2048,
  width: null, height: null, durationMs: null, waveform: null, hasThumbnail: false, state: 'stored', ...extra,
})

it('a file is a download link with its name; an expired one says so', async () => {
  const wrapper = mount(AttachmentList, {
    props: { attachments: [file('doc'), file('gone', { state: 'expired' })] },
    global: { plugins: [createPinia()] },
  })
  await flushPromises()

  const link = wrapper.get('a.name')
  expect(link.attributes('href')).toBe('https://s/doc')
  expect(link.attributes('download')).toBe('doc.pdf')
  expect(link.attributes('rel')).toContain('noopener')
  expect(wrapper.text()).toContain('Файл больше не хранится')
})

it('photos show their previews; a click opens the viewer with the original', async () => {
  const photo = file('p1', { kind: 'photo', hasThumbnail: true, width: 1600, height: 900 })
  const wrapper = mount(AttachmentList, { props: { attachments: [photo] }, global: { plugins: [createPinia()] } })
  await flushPromises()

  expect(wrapper.get('.tile img').attributes('src')).toBe('https://s/p1.t')
  await wrapper.get('.tile').trigger('click')
  expect(wrapper.get('.viewer img').attributes('src')).toBe('https://s/p1')
})
