import { flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed } from 'vue'

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

afterEach(() => vi.useRealTimers())

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

  it('a fresh link is not fetched again; one about to expire is, after a pause', async () => {
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] })
    const store = useMediaLinksStore()
    vi.mocked(mediaApi.getLinks).mockResolvedValueOnce({ items: [link('a', new Date(Date.now() + 30_000).toISOString())] })
    store.get('a')
    await flushPromises()

    vi.advanceTimersByTime(30_000)
    store.get('a')
    await flushPromises()

    expect(mediaApi.getLinks).toHaveBeenCalledTimes(2)
    store.get('a')
    await flushPromises()
    expect(mediaApi.getLinks).toHaveBeenCalledTimes(2)
  })

  it('an id the server leaves out is not asked for on every render', async () => {
    // The bug: every answer replaced the cache, every render asked again for the missing photo —
    // one request after another until the page was reloaded.
    vi.mocked(mediaApi.getLinks).mockImplementation(async (ids) => ({ items: ids.filter((id) => id !== 'hidden').map((id) => link(id)) }))
    const store = useMediaLinksStore()
    const shown = computed(() => [store.get('hidden'), store.get('a')])

    for (let i = 0; i < 5; i++) {
      void shown.value
      await flushPromises()
    }

    expect(mediaApi.getLinks).toHaveBeenCalledTimes(1)
  })

  it('a link that expires while the page sits idle is fetched again by itself', async () => {
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] })
    const store = useMediaLinksStore()
    const url = computed(() => store.get('a')?.url)
    void url.value
    await flushPromises()

    vi.advanceTimersByTime(3_600_000)
    void url.value
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
