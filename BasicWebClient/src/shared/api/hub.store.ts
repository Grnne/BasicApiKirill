/**
 * Соединение с SignalR-хабом.
 *
 * Стор держит одно соединение на всё приложение и берёт на себя три вещи,
 * в которых легко ошибиться:
 *  1) токен запрашивается в момент подключения, а не захватывается заранее;
 *  2) после реконнекта чат перезаходится сам — группы на сервере не переживают
 *     разрыв, и без этого сообщения молча перестают приходить;
 *  3) подписки переживают пересоздание соединения.
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

/** Паузы перед попытками переподключения, мс. Дальше — сдаёмся и ждём действий пользователя. */
const RECONNECT_DELAYS = [0, 2_000, 5_000, 10_000, 30_000]

type AnyHandler = (...args: never[]) => void

export const useHubStore = defineStore('hub', () => {
  const status = ref<ConnectionStatus>('disconnected')
  const connection = shallowRef<HubConnection | null>(null)

  /** Чат, в группу которого мы вошли. Нужен, чтобы вернуться в неё после разрыва. */
  const joinedChatId = ref<string | null>(null)

  /** Подписки фич. Хранятся отдельно от соединения, чтобы пережить его пересоздание. */
  const listeners = new Map<HubEventName, Set<AnyHandler>>()

  function listenersFor(event: HubEventName): Set<AnyHandler> {
    let set = listeners.get(event)
    if (!set) {
      set = new Set()
      listeners.set(event, set)
    }
    return set
  }

  /**
   * Подписка на событие хаба. Возвращает функцию отписки — её удобно вызвать
   * в onUnmounted компонента.
   */
  function on<K extends HubEventName>(event: K, handler: HubEvents[K]): () => void {
    listenersFor(event).add(handler as AnyHandler)
    return () => {
      listenersFor(event).delete(handler as AnyHandler)
    }
  }

  /**
   * Соединение вызывает подписчиков через эту прослойку, а не напрямую.
   * Поэтому on() работает и до подключения, и после реконнекта, и не надо
   * ничего переподписывать вручную.
   */
  function attachEvents(hub: HubConnection): void {
    for (const event of HUB_EVENT_NAMES) {
      hub.on(event, (...args: never[]) => {
        for (const handler of listenersFor(event)) {
          try {
            handler(...args)
          } catch (error) {
            // Ошибка одного подписчика не должна ронять остальных.
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
        /**
         * Вызывается на каждом подключении и переподключении — поэтому здесь
         * функция, а не готовая строка. Если access-токен уже истёк (например,
         * вкладка была свёрнута), сначала обновляем пару.
         */
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
      // Группы чатов живут на конкретном соединении и после разрыва теряются.
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

  /**
   * Вызов метода хаба. Ошибки не пробрасываем: сервер может ответить
   * HubException (например, при превышении лимита вызовов), и валить на этом
   * интерфейс незачем — возвращаем false.
   */
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

  /* ── Методы хаба ── */

  async function joinChat(chatId: string): Promise<void> {
    if (joinedChatId.value === chatId) return
    if (joinedChatId.value) await invokeSafe('LeaveChat', joinedChatId.value)

    // Запоминаем до вызова: если соединение сейчас лежит, зайдём при реконнекте.
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
