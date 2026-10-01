/*
 * The single SignalR connection. The token is fetched on each (re)connect rather than captured;
 * the open chat is rejoined after a reconnect, because server groups do not survive a dropped
 * connection and messages would silently stop; subscriptions survive connection re-creation.
 */

import { ref, shallowRef } from 'vue'
import { defineStore } from 'pinia'
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr'

import { getAuthBridge } from './http'
import {
  HUB_EVENT_NAMES,
  type ConnectionStatus,
  type HubEventName,
  type HubEvents,
} from './hub.types'

const HUB_URL = '/hubs/chat'

/** After the last delay the client gives up and stays disconnected. */
const RECONNECT_DELAYS = [0, 2_000, 5_000, 10_000, 30_000]

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
            console.error(`Обработчик ${event} упал:`, error)
          }
        }
      })
    }
  }

  async function start(): Promise<void> {
    if (connection.value || status.value === 'connecting') return

    status.value = 'connecting'

    const hub = new HubConnectionBuilder()
      .withUrl(HUB_URL, {
        // Called on every connect and reconnect, so the token is always current.
        accessTokenFactory: async () => {
          const bridge = getAuthBridge()
          if (!bridge) return ''

          if (!bridge.getAccessToken()) await bridge.refreshTokens()
          return bridge.getAccessToken() ?? ''
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
      status.value = 'disconnected'
      connection.value = null
    })

    try {
      await hub.start()
      connection.value = hub
      status.value = 'connected'
      if (joinedChatId.value) await invokeSafe('JoinChat', joinedChatId.value)
    } catch (error) {
      status.value = 'disconnected'
      connection.value = null
      console.error('Не удалось подключиться к хабу:', error)
    }
  }

  async function stop(): Promise<void> {
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
      console.error(`Вызов ${method} не прошёл:`, error)
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

  function sendMessage(chatId: string, text: string): Promise<boolean> {
    return invokeSafe('SendMessage', chatId, text)
  }

  function sendTyping(chatId: string, isTyping: boolean): Promise<boolean> {
    return invokeSafe('Typing', chatId, isTyping)
  }

  return {
    status,
    joinedChatId,
    on,
    start,
    stop,
    joinChat,
    leaveChat,
    sendMessage,
    sendTyping,
  }
})
