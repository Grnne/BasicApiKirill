/*
 * The single SignalR connection. The token is fetched (and refreshed if stale) on each (re)connect;
 * the open chat is rejoined after a reconnect, because server groups do not survive a dropped
 * connection; subscriptions survive connection re-creation. When SignalR's own reconnect gives up,
 * a retry loop keeps trying for as long as the user is logged in.
 */

import { ref, shallowRef } from 'vue'
import { defineStore } from 'pinia'
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr'

import { RetryLoop } from '@/shared/lib/retry'
import { getAuthBridge } from './http'
import { freshAccessToken } from './token'
import {
  HUB_EVENT_NAMES,
  type ConnectionStatus,
  type HubEventName,
  type HubEvents,
} from './hub.types'

const HUB_URL = '/hubs/chat'

/** SignalR's own reconnect; after it gives up, RetryLoop takes over. */
const RECONNECT_DELAYS = [0, 2_000, 5_000, 10_000]

type AnyHandler = (...args: never[]) => void

export const useHubStore = defineStore('hub', () => {
  const status = ref<ConnectionStatus>('disconnected')
  const connection = shallowRef<HubConnection | null>(null)

  /** Kept to rejoin the group after a reconnect. */
  const joinedChatId = ref<string | null>(null)

  /** Kept apart from the connection so they survive its re-creation. */
  const listeners = new Map<HubEventName, Set<AnyHandler>>()

  function listenersFor(event: HubEventName): Set<AnyHandler> {
    let set = listeners.get(event)
    if (!set) {
      set = new Set()
      listeners.set(event, set)
    }
    return set
  }

  /** Works before connecting and across reconnects; returns an unsubscribe function. */
  function on<K extends HubEventName>(event: K, handler: HubEvents[K]): () => void {
    listenersFor(event).add(handler as AnyHandler)
    return () => {
      listenersFor(event).delete(handler as AnyHandler)
    }
  }

  function attachEvents(hub: HubConnection): void {
    for (const event of HUB_EVENT_NAMES) {
      hub.on(event, (...args: never[]) => {
        for (const handler of listenersFor(event)) {
          try {
            handler(...args)
          } catch (error) {
            // One failing handler must not break the others.
            console.error(`Hub handler ${event} failed:`, error)
          }
        }
      })
    }
  }

  /** true between start() and stop(): the connection should be up. */
  let wanted = false
  const retry = new RetryLoop(connect)

  function retryNow(): void {
    if (wanted && !connection.value && status.value !== 'connecting') retry.now()
  }

  function onBrowserBack(): void {
    if (document.visibilityState === 'visible') retryNow()
  }

  async function start(): Promise<void> {
    if (wanted) return
    wanted = true
    window.addEventListener('online', retryNow)
    document.addEventListener('visibilitychange', onBrowserBack)
    if (!(await connect())) retry.schedule()
  }

  async function connect(): Promise<boolean> {
    if (!wanted) return true
    if (connection.value || status.value === 'connecting') return true

    status.value = 'connecting'

    const hub = new HubConnectionBuilder()
      .withUrl(HUB_URL, {
        // Called on every connect and reconnect: a token that expired while the tab was idle
        // would get 401 and end the session's realtime until F5.
        accessTokenFactory: async () => {
          const bridge = getAuthBridge()
          return bridge ? freshAccessToken(bridge) : ''
        },
      })
      .withAutomaticReconnect(RECONNECT_DELAYS)
      .configureLogging(import.meta.env.DEV ? LogLevel.Warning : LogLevel.Error)
      .build()

    attachEvents(hub)

    hub.onreconnecting(() => {
      status.value = 'reconnecting'
    })

    hub.onreconnected(() => {
      status.value = 'connected'
      // Groups belong to a connection and are lost when it drops.
      if (joinedChatId.value) void invokeSafe('JoinChat', joinedChatId.value)
    })

    hub.onclose(() => {
      if (connection.value !== hub) return
      status.value = 'disconnected'
      connection.value = null
      // SignalR's reconnect gave up (or the server closed it): keep trying while logged in.
      if (wanted) retry.schedule()
    })

    try {
      await hub.start()
      if (!wanted) {
        await hub.stop()
        return true
      }
      connection.value = hub
      status.value = 'connected'
      if (joinedChatId.value) await invokeSafe('JoinChat', joinedChatId.value)
      return true
    } catch (error) {
      status.value = 'disconnected'
      console.warn('Hub connection failed:', error)
      return false
    }
  }

  async function stop(): Promise<void> {
    wanted = false
    retry.cancel()
    window.removeEventListener('online', retryNow)
    document.removeEventListener('visibilitychange', onBrowserBack)
    const hub = connection.value
    connection.value = null
    joinedChatId.value = null
    status.value = 'disconnected'
    if (hub) await hub.stop()
  }

  /** Never throws: a HubException (e.g. rate limit) becomes false instead of breaking the UI. */
  async function invokeSafe(method: string, ...args: unknown[]): Promise<boolean> {
    const hub = connection.value
    if (!hub || hub.state !== HubConnectionState.Connected) return false

    try {
      await hub.invoke(method, ...args)
      return true
    } catch (error) {
      console.error(`Hub call ${method} failed:`, error)
      return false
    }
  }

  async function joinChat(chatId: string): Promise<void> {
    if (joinedChatId.value === chatId) return
    if (joinedChatId.value) await invokeSafe('LeaveChat', joinedChatId.value)

    // Recorded before the call: if the connection is down, the chat is joined on reconnect.
    joinedChatId.value = chatId
    await invokeSafe('JoinChat', chatId)
  }

  async function leaveChat(): Promise<void> {
    const chatId = joinedChatId.value
    joinedChatId.value = null
    if (chatId) await invokeSafe('LeaveChat', chatId)
  }

  return {
    status,
    joinedChatId,
    on,
    start,
    stop,
    retryNow,
    joinChat,
    leaveChat,
  }
})
