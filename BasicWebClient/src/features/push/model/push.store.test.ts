import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import * as pushApi from '../api/push.api'
import * as browser from '../lib/browser'
import { base64UrlToBytes } from '../lib/browser'
import { usePushStore } from './push.store'

vi.mock('../api/push.api', () => ({
  getPushConfig: vi.fn(),
  saveSubscription: vi.fn(async () => {}),
  deleteSubscription: vi.fn(async () => {}),
}))
vi.mock('../lib/browser', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../lib/browser')>()),
  isSupported: vi.fn(() => true),
  permission: vi.fn(() => 'default'),
  requestPermission: vi.fn(),
  currentSubscription: vi.fn(async () => null),
  subscribe: vi.fn(),
}))

const subscription = () => {
  const json = { endpoint: 'https://fcm.googleapis.com/fcm/send/x', keys: { p256dh: 'p', auth: 'a' } }
  return { toJSON: () => json, unsubscribe: vi.fn(async () => true), json } as unknown as PushSubscription & {
    json: PushSubscriptionJSON
  }
}

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
  vi.mocked(pushApi.getPushConfig).mockResolvedValue({ enabled: true, vapidPublicKey: 'BKey' })
  vi.mocked(browser.permission).mockReturnValue('default')
  vi.mocked(browser.currentSubscription).mockResolvedValue(null)
})

describe('push', () => {
  it('the state: no browser support, push off on the server, forbidden, or whether subscribed', async () => {
    const push = usePushStore()
    vi.mocked(browser.isSupported).mockReturnValueOnce(false)
    await push.refresh()
    expect(push.state).toBe('unsupported')

    vi.mocked(pushApi.getPushConfig).mockResolvedValueOnce({ enabled: false, vapidPublicKey: null })
    await push.refresh()
    expect(push.state).toBe('unavailable')

    vi.mocked(browser.permission).mockReturnValue('denied')
    await push.refresh()
    expect(push.state).toBe('denied')

    vi.mocked(browser.permission).mockReturnValue('granted')
    vi.mocked(browser.currentSubscription).mockResolvedValueOnce(subscription())
    await push.refresh()
    expect(push.state).toBe('on')
  })

  it('turned on by the user: permission, a subscription with the server key, given to the server', async () => {
    const sub = subscription()
    vi.mocked(browser.requestPermission).mockResolvedValue('granted')
    vi.mocked(browser.subscribe).mockResolvedValue(sub)
    const push = usePushStore()

    await push.enable()

    expect(browser.subscribe).toHaveBeenCalledWith('BKey')
    expect(pushApi.saveSubscription).toHaveBeenCalledWith(sub.json)
    expect(push.state).toBe('on')
  })

  it('a refusal subscribes nothing', async () => {
    vi.mocked(browser.requestPermission).mockResolvedValue('denied')
    const push = usePushStore()

    await push.enable()

    expect(browser.subscribe).not.toHaveBeenCalled()
    expect(push.state).toBe('denied')
  })

  it('after sign-in the subscription goes to the new sign-in; nothing is asked of the user', async () => {
    const sub = subscription()
    vi.mocked(browser.permission).mockReturnValue('granted')
    vi.mocked(browser.currentSubscription).mockResolvedValue(sub)

    await usePushStore().resync()

    expect(pushApi.saveSubscription).toHaveBeenCalledWith(sub.json)
    expect(browser.requestPermission).not.toHaveBeenCalled()
  })

  it('logout: the server and the browser both forget the subscription, and a failure does not stop it', async () => {
    const sub = subscription()
    vi.mocked(browser.currentSubscription).mockResolvedValue(sub)
    vi.mocked(pushApi.deleteSubscription).mockRejectedValueOnce(new Error('offline'))

    await usePushStore().forget()

    expect(sub.unsubscribe).toHaveBeenCalled()
  })

  it('a browser that refuses the subscription is named, not "something went wrong"', async () => {
    // Seen in a browser with its push service off: "Registration failed - permission denied".
    vi.mocked(browser.requestPermission).mockResolvedValue('granted')
    vi.mocked(browser.subscribe).mockRejectedValue(new DOMException('Registration failed - permission denied', 'AbortError'))
    const push = usePushStore()

    await push.enable()

    expect(push.error).toBe('Браузер не смог подписаться на уведомления — возможно, они выключены в его настройках')
    expect(push.state).not.toBe('on')
  })
})

it('a VAPID key comes as base64url and goes to the browser as bytes', () => {
  expect([...base64UrlToBytes('AQID_-8')]).toEqual([1, 2, 3, 255, 239])
})
