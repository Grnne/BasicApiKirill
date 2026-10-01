import { ref } from 'vue'
import { defineStore } from 'pinia'

import { describeError } from '@/shared/api/problem'
import * as pushApi from '../api/push.api'
import * as browser from '../lib/browser'

/**
 * unsupported — the browser cannot; unavailable — the server sends no push; denied — the user
 * forbade notifications for the site; off / on — this device is (not) subscribed.
 */
export type PushState = 'unsupported' | 'unavailable' | 'denied' | 'off' | 'on'

/** Push notifications of this device. Turned on only by the user's own click (enable). */
export const usePushStore = defineStore('push', () => {
  const state = ref<PushState>('off')
  const busy = ref(false)
  const error = ref<string | null>(null)

  async function refresh(): Promise<void> {
    if (!browser.isSupported()) {
      state.value = 'unsupported'
      return
    }
    try {
      if (!(await pushApi.getPushConfig()).enabled) {
        state.value = 'unavailable'
        return
      }
    } catch (e) {
      error.value = describeError(e)
      return
    }
    if (browser.permission() === 'denied') {
      state.value = 'denied'
      return
    }
    // Rejects where the site may not keep data; the page calls refresh without waiting.
    const subscription = await browser.currentSubscription().catch(() => null)
    state.value = subscription && browser.permission() === 'granted' ? 'on' : 'off'
  }

  async function enable(): Promise<void> {
    if (busy.value) return
    busy.value = true
    error.value = null
    try {
      const config = await pushApi.getPushConfig()
      if (!config.enabled || !config.vapidPublicKey) {
        state.value = 'unavailable'
        return
      }
      const answer = await browser.requestPermission()
      if (answer !== 'granted') {
        state.value = answer === 'denied' ? 'denied' : 'off'
        return
      }
      let subscription: PushSubscription
      try {
        subscription = await browser.subscribe(config.vapidPublicKey)
      } catch {
        // The browser's own push service said no (switched off, blocked by a policy, private mode).
        error.value = 'Браузер не смог подписаться на уведомления — возможно, они выключены в его настройках'
        return
      }
      await pushApi.saveSubscription(subscription.toJSON())
      state.value = 'on'
    } catch (e) {
      error.value = describeError(e)
    } finally {
      busy.value = false
    }
  }

  async function disable(): Promise<void> {
    if (busy.value) return
    busy.value = true
    error.value = null
    try {
      await pushApi.deleteSubscription()
      await (await browser.currentSubscription())?.unsubscribe()
      state.value = 'off'
    } catch (e) {
      error.value = describeError(e)
    } finally {
      busy.value = false
    }
  }

  /**
   * After sign-in: a subscription this browser already holds is given to the new sign-in (the
   * server moves it), so notifications follow the user who is signed in now.
   */
  async function resync(): Promise<void> {
    if (browser.permission() !== 'granted') return
    try {
      const subscription = await browser.currentSubscription()
      if (subscription) await pushApi.saveSubscription(subscription.toJSON())
    } catch {
      // Push is a convenience: the next sign-in or a click in the settings tries again.
    }
  }

  /** Before logout: no notifications for a user who has left this browser. Never throws. */
  async function forget(): Promise<void> {
    try {
      const subscription = await browser.currentSubscription()
      if (!subscription) return
      await pushApi.deleteSubscription().catch(() => {})
      await subscription.unsubscribe()
    } catch {
      // Logout goes on; the server drops the subscription with the sign-in anyway.
    } finally {
      state.value = 'off'
    }
  }

  return { state, busy, error, refresh, enable, disable, resync, forget }
})
