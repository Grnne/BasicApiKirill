import { computed, ref } from 'vue'
import { defineStore } from 'pinia'

import { describeError } from '@/shared/api/problem'
import * as pushApi from '../api/push.api'
import * as browser from '../lib/browser'
import * as prefs from '../lib/prefs'

/**
 * unsupported — the browser cannot; unavailable — the server sends no push; denied — the user
 * forbade notifications for the site; off / on — this device is (not) subscribed.
 */
export type PushState = 'unsupported' | 'unavailable' | 'denied' | 'off' | 'on'

/**
 * Notifications of this device: push while the client is closed, and the browser's permission the
 * open client shows its own with. On by default — asked on the first click after sign-in — until
 * the user turns them off here.
 */
export const usePushStore = defineStore('push', () => {
  const state = ref<PushState>('off')
  const busy = ref(false)
  const error = ref<string | null>(null)
  const turnedOff = ref(prefs.turnedOff())
  const permission = ref<NotificationPermission>(browser.permission())
  let asked = false

  /** The open client may show notifications: allowed by the browser and not turned off here. */
  const notifying = computed(() => browser.isSupported() && permission.value === 'granted' && !turnedOff.value)

  async function refresh(): Promise<void> {
    if (!browser.isSupported()) {
      state.value = 'unsupported'
      return
    }
    // Forbidden in the browser comes first: the open client cannot show notifications either.
    permission.value = browser.permission()
    if (permission.value === 'denied') {
      state.value = 'denied'
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
    // Rejects where the site may not keep data; the page calls refresh without waiting.
    const subscription = await browser.currentSubscription().catch(() => null)
    state.value = subscription && browser.permission() === 'granted' ? 'on' : 'off'
  }

  /** The permission first: the open client shows notifications even where the server sends no push. */
  async function enable(): Promise<void> {
    if (busy.value) return
    busy.value = true
    error.value = null
    try {
      turnedOff.value = false
      prefs.setTurnedOff(false)
      const answer = browser.permission() === 'granted' ? 'granted' : await browser.requestPermission()
      permission.value = answer
      if (answer !== 'granted') {
        state.value = answer === 'denied' ? 'denied' : 'off'
        return
      }
      const config = await pushApi.getPushConfig()
      if (!config.enabled || !config.vapidPublicKey) {
        state.value = 'unavailable'
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

  /** Off on this device, push and the open client's notifications alike, until turned on again. */
  async function disable(): Promise<void> {
    if (busy.value) return
    busy.value = true
    error.value = null
    turnedOff.value = true
    prefs.setTurnedOff(true)
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
   * server moves it), so notifications follow the user who is signed in now. Without one — the
   * permission was given before, logout dropped the subscription — it subscribes again, unasked.
   */
  async function resync(): Promise<void> {
    if (browser.permission() !== 'granted') return
    try {
      const subscription = await browser.currentSubscription()
      if (subscription) await pushApi.saveSubscription(subscription.toJSON())
      else if (!turnedOff.value) await enable()
    } catch {
      // Push is a convenience: the next sign-in or a click in the settings tries again.
    }
  }

  /**
   * On by default: a browser lets a page ask for the permission only in answer to a click, so the
   * first click after sign-in asks — once per page load, never after the user turned them off.
   */
  async function askOnce(): Promise<void> {
    if (asked || turnedOff.value || !browser.isSupported() || browser.permission() !== 'default') return
    asked = true
    await enable()
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

  return { state, busy, error, notifying, refresh, enable, disable, resync, askOnce, forget }
})
