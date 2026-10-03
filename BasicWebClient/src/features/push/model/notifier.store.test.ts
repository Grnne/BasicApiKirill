import { flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { useChatsStore } from '@/entities/chat/model/chats.store'
import { useAccountStore } from '@/entities/user/model/account.store'
import { useSessionStore } from '@/entities/user/model/session.store'
import type { PushNotificationDto } from '@/shared/api/schema'
import { BOB, ME, chat, message } from '@/testing/fixtures'
import * as browser from '../lib/browser'
import * as sound from '../lib/sound'
import { useNotifierStore } from './notifier.store'

const handlers: Record<string, (...args: never[]) => void> = {}
vi.mock('@/shared/api/hub.store', () => ({
  useHubStore: () => ({
    on: (event: string, handler: (...args: never[]) => void) => {
      handlers[event] = handler
      return () => delete handlers[event]
    },
  }),
}))
vi.mock('../lib/browser', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../lib/browser')>()),
  isSupported: vi.fn(() => true),
  permission: vi.fn(() => 'granted'),
  show: vi.fn(async () => {}),
}))
vi.mock('../lib/sound', () => ({ chime: vi.fn(), unlock: vi.fn() }))

const emit = (event: string, ...args: unknown[]) => handlers[event]!(...(args as never[]))

let focused = false
let openChat: string | null = null

function start() {
  useSessionStore().user = { userId: ME, username: 'me', email: 'me@test', displayName: 'Me', avatarId: null }
  useChatsStore().replaceAll(
    [
      chat({ chatId: 'chat-1', unreadCount: 2 }),
      chat({ chatId: 'group-1', type: 'group', title: 'Команда', companionId: null, unreadCount: 1 }),
      chat({ chatId: 'quiet-1', isMuted: true, mutedUntil: null, unreadCount: 5 }),
    ],
    [],
  )
  const notifier = useNotifierStore()
  notifier.start(() => openChat)
  return notifier
}

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
  localStorage.clear()
  focused = false
  openChat = null
  vi.spyOn(document, 'hasFocus').mockImplementation(() => focused)
  document.title = 'Basic Chat'
})
afterEach(() => useNotifierStore().stop())

describe('the open client, its tab not in front', () => {
  it('shows a new message as push would: the group, the sender, the text', () => {
    start()

    emit('ChatListUpdated', 'group-1', message({ chatId: 'group-1', senderName: 'Bob', text: 'релиз в пятницу' }))

    expect(browser.show).toHaveBeenCalledWith(expect.objectContaining({
      kind: 'message', chatId: 'group-1', chatType: 'group', chatTitle: 'Команда', senderName: 'Bob', text: 'релиз в пятницу',
    }))
  })

  it('a reaction to my message comes ready from the server', () => {
    start()
    const reaction = { kind: 'reaction', chatId: 'chat-1', senderId: BOB, emoji: '🔥' } as PushNotificationDto

    emit('Notification', reaction)

    expect(browser.show).toHaveBeenCalledWith(reaction)
  })

  it('nothing of my own messages, muted chats or people I blocked', () => {
    start()
    useAccountStore().blocked = new Set(['carl'])

    emit('ChatListUpdated', 'chat-1', message({ senderId: ME }))
    emit('ChatListUpdated', 'quiet-1', message({ chatId: 'quiet-1' }))
    emit('ChatListUpdated', 'group-1', message({ chatId: 'group-1', senderId: 'carl' }))

    expect(browser.show).not.toHaveBeenCalled()
  })

  it('nothing once the user turned notifications off here', () => {
    localStorage.setItem('notifications.off', '1')
    start()

    emit('ChatListUpdated', 'chat-1', message())

    expect(browser.show).not.toHaveBeenCalled()
  })
})

describe('the tab in front', () => {
  it('no system notification; the sound, when on, only for a chat other than the open one', () => {
    focused = true
    openChat = 'chat-1'
    const notifier = start()
    notifier.setSound(true)

    emit('ChatListUpdated', 'chat-1', message())
    expect(sound.chime).not.toHaveBeenCalled()

    emit('ChatListUpdated', 'group-1', message({ chatId: 'group-1' }))
    expect(sound.chime).toHaveBeenCalledOnce()
    expect(browser.show).not.toHaveBeenCalled()
  })

  it('no sound unless the user asked for it', () => {
    start()

    emit('ChatListUpdated', 'chat-1', message())

    expect(sound.chime).not.toHaveBeenCalled()
  })
})

it('the tab title counts unread messages of the chats that notify, and is given back on logout', async () => {
  const notifier = start()
  expect(document.title).toBe('(3) Basic Chat')

  useChatsStore().patch('chat-1', { unreadCount: 0 })
  useChatsStore().patch('group-1', { unreadCount: 0 })
  await flushPromises()
  expect(document.title).toBe('Basic Chat')

  useChatsStore().patch('chat-1', { unreadCount: 1 })
  await flushPromises()
  notifier.stop()
  expect(document.title).toBe('Basic Chat')
})
