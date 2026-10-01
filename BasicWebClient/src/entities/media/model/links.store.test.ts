import { flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import * as mediaApi from '../api'
import { isFresh, useMediaLinksStore } from './links.store'

vi.mock('../api', () => ({ getLinks: vi.fn() }))

const link = (attachmentId: string, expiresAt = new Date(Date.now() + 3_600_000).toISOString()) =>
  ({ attachmentId, url: `https://s/${attachmentId}`, thumbnailUrl: `https://s/${attachmentId}.t`, expiresAt })

beforeEach(() => {
  setActivePinia(createPinia())
  vi.mocked(mediaApi.getLinks).mockReset()
  vi.mocked(mediaApi.getLinks).mockImplementation(async (ids) => ({ items: ids.map((id) => link(id)) }))
})

describe('media links', () => {
  it('asks of one render go as one request; the links then come reactively', async () => {
    const store = useMediaLinksStore()

    expect(store.get('a')).toBeNull()
    expect(store.get('b')).toBeNull()
    await flushPromises()

    expect(mediaApi.getLinks).toHaveBeenCalledTimes(1)
    expect(mediaApi.getLinks).toHaveBeenCalledWith(['a', 'b'])
    expect(store.get('a')!.url).toBe('https://s/a')
  })

  it('a fresh link is not fetched again; one about to expire is', async () => {
    const store = useMediaLinksStore()
    vi.mocked(mediaApi.getLinks).mockResolvedValueOnce({ items: [link('a', new Date(Date.now() + 30_000).toISOString())] })
    store.get('a')
    await flushPromises()

    store.get('a')
    await flushPromises()

    expect(mediaApi.getLinks).toHaveBeenCalledTimes(2)
    store.get('a')
    await flushPromises()
    expect(mediaApi.getLinks).toHaveBeenCalledTimes(2)
  })

  it('isFresh keeps a minute of margin', () => {
    const now = Date.UTC(2026, 9, 1, 12)
    expect(isFresh(link('a', '2026-10-01T12:05:00Z'), now)).toBe(true)
    expect(isFresh(link('a', '2026-10-01T12:00:30Z'), now)).toBe(false)
    expect(isFresh(undefined, now)).toBe(false)
  })
})
