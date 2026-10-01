import { flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { reactive } from 'vue'

import * as presenceApi from './presence.api'
import { usePresenceStore } from './presence.store'

const hub = reactive({ status: 'connected', on: () => () => {} })
vi.mock('@/shared/api/hub.store', () => ({ useHubStore: () => hub }))
vi.mock('./presence.api', () => ({ getUsersStatus: vi.fn(), getTypingStatus: vi.fn(async () => ({ items: [] })) }))

let onlineNow: Record<string, boolean> = {}

beforeEach(() => {
  setActivePinia(createPinia())
  hub.status = 'connected'
  onlineNow = { bob: true }
  vi.mocked(presenceApi.getUsersStatus).mockReset()
  vi.mocked(presenceApi.getUsersStatus).mockImplementation(async (ids) => ({
    items: ids.map((userId) => ({ userId, isOnline: onlineNow[userId] ?? false })),
  }) as never)
})

describe('online statuses', () => {
  it('are asked again after the connection was down: changes then were missed', async () => {
    // The bug: statuses came only with the snapshot; after a laptop's sleep Bob, gone meanwhile,
    // stayed "online".
    const presence = usePresenceStore()
    presence.subscribeToHub()
    await presence.loadStatuses(['bob'])
    expect(presence.isOnline('bob')).toBe(true)

    onlineNow = { bob: false }
    hub.status = 'reconnecting'
    await flushPromises()
    hub.status = 'connected'
    await flushPromises()

    expect(presence.isOnline('bob')).toBe(false)
  })

  it('a companion of a chat that appears later gets a status too', async () => {
    // The bug: a new private chat with someone online said "не в сети".
    const presence = usePresenceStore()

    await presence.track(['bob'])

    expect(presence.isOnline('bob')).toBe(true)
    await presence.track(['bob'])
    expect(presenceApi.getUsersStatus).toHaveBeenCalledTimes(1)
  })
})
