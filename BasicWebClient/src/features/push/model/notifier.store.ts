import { computed, ref, watch } from 'vue'
import { defineStore } from 'pinia'

import { isMutedNow } from '@/entities/chat/lib'
import { useChatsStore } from '@/entities/chat/model/chats.store'
import { useAccountStore } from '@/entities/user/model/account.store'
import { useSessionStore } from '@/entities/user/model/session.store'
import { useHubStore } from '@/shared/api/hub.store'
import type { MessageDto, PushNotificationDto } from '@/shared/api/schema'
import * as browser from '../lib/browser'
import * as prefs from '../lib/prefs'
import { chime, unlock } from '../lib/sound'
import { usePushStore } from './push.store'

/**
 * What the open client tells the user about, as push does while it is closed: new messages and
 * reactions to their messages. A system notification while the tab is not on screen, an optional
 * sound, and the unread count in the tab's title.
 */
export const useNotifierStore = defineStore('notifier', () => {
  const hub = useHubStore()
  const chats = useChatsStore()
  const account = useAccountStore()
  const session = useSessionStore()
  const push = usePushStore()

  const sound = ref(prefs.sound())
  let openChatId: () => string | null = () => null
  let stops: (() => void)[] = []
  let title = ''

  /** Unread messages of the chats that notify; the muted ones stay quiet in the title too. */
  const unread = computed(() =>
    chats.list.reduce((sum, chat) => (isMutedNow(chat) ? sum : sum + chat.unreadCount), 0),
  )

  function setSound(on: boolean): void {
    sound.value = on
    prefs.setSound(on)
    if (on) unlock()
  }

  /** From a click: browsers start audio only in answer to one. */
  function onClick(): void {
    if (sound.value) unlock()
  }

  function notify(notification: PushNotificationDto): void {
    if (notification.senderId === session.user?.userId) return
    const chat = chats.get(notification.chatId)
    if ((chat && isMutedNow(chat)) || account.isBlocked(notification.senderId)) return

    const away = document.visibilityState !== 'visible' || !document.hasFocus()
    if (away && push.notifying) void browser.show(notification)
    if (sound.value && (away || openChatId() !== notification.chatId)) chime()
  }

  /** A new message, from the chat list's preview: the same fields push carries. */
  function onMessage(message: MessageDto): void {
    const chat = chats.get(message.chatId)
    notify({
      kind: 'message',
      chatId: message.chatId,
      chatType: chat?.type ?? 'private',
      chatTitle: chat?.type === 'group' ? chat.title : null,
      messageId: message.id,
      seq: message.seq,
      senderId: message.senderId,
      senderName: message.senderName,
      messageType: message.type,
      text: message.text,
      attachmentKind: message.attachments[0]?.kind ?? null,
      attachmentCount: message.attachments.length,
      emoji: null,
      createdAt: message.createdAt,
    })
  }

  /** After sign-in; openChat — the chat on screen: its messages make no sound while the tab is in front. */
  function start(openChat: () => string | null): void {
    stop()
    openChatId = openChat
    title = document.title
    stops = [
      hub.on('ChatListUpdated', (_chatId, message) => onMessage(message)),
      hub.on('Notification', notify),
      watch(unread, (count) => (document.title = count > 0 ? `(${count}) ${title}` : title), { immediate: true }),
    ]
    document.addEventListener('click', onClick, true)
  }

  function stop(): void {
    for (const off of stops) off()
    stops = []
    document.removeEventListener('click', onClick, true)
    if (title) document.title = title
  }

  return { sound, unread, setSound, start, stop }
})
