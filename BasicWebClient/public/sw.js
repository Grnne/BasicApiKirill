// Service worker of the web client: shows notifications about new messages and reactions to the
// user's messages — from push when the client is closed, from the open client when its tab is not
// on screen — and opens the chat on a click. It holds no access token: everything shown comes in the notification itself
// (docs/api-contract-changes.md, 18.3). Plain JavaScript, copied to the build as is.

const KIND_LABELS = { photo: 'Фото', video: 'Видео', voice: 'Голосовое сообщение', file: 'Файл' }

/** The text, or for a file without a caption — what kind and how many. */
function bodyOf(payload) {
  const text = typeof payload.text === 'string' ? payload.text.trim() : ''
  if (text) return text
  const label = KIND_LABELS[payload.attachmentKind] || 'Сообщение'
  return payload.attachmentCount > 1 ? `${label} (${payload.attachmentCount})` : label
}

self.addEventListener('install', () => self.skipWaiting())
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()))

/** Shows a notification of the push payload's shape (PushNotificationDto); an unknown kind is skipped. */
function show(payload) {
  if (!payload || !payload.chatId || (payload.kind !== 'message' && payload.kind !== 'reaction')) {
    return Promise.resolve()
  }

  const group = payload.chatType === 'group'
  const reaction = payload.kind === 'reaction'
  const title = group ? payload.chatTitle || 'Группа' : payload.senderName || 'Новое сообщение'
  const text = reaction ? `${payload.emoji} на «${bodyOf(payload)}»` : bodyOf(payload)
  const body = group && (reaction || payload.messageType !== 'system') ? `${payload.senderName}: ${text}` : text

  return self.registration.showNotification(title, {
    body,
    // One notification per chat: a newer one replaces the older — also the same one shown by
    // several open tabs. A reaction replaces only an older reaction to the same message.
    tag: reaction ? `reaction-${payload.messageId}` : `chat-${payload.chatId}`,
    renotify: true,
    data: { chatId: payload.chatId },
  })
}

self.addEventListener('push', (event) => {
  let payload = null
  try {
    payload = event.data ? event.data.json() : null
  } catch {
    payload = null
  }
  event.waitUntil(show(payload))
})

// The open client, its tab not on screen, asks for the same notification push would show.
self.addEventListener('message', (event) => {
  const data = event.data
  if (data && data.type === 'show') event.waitUntil(show(data.payload))
})

self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  const chatId = event.notification.data && event.notification.data.chatId
  const scope = self.registration.scope

  event.waitUntil(
    (async () => {
      const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true })
      const open = windows.find((w) => w.url.startsWith(scope))
      if (open) {
        await open.focus()
        if (chatId) open.postMessage({ type: 'open-chat', chatId })
        return
      }
      await self.clients.openWindow(chatId ? `${scope}chat?open=${encodeURIComponent(chatId)}` : `${scope}chat`)
    })(),
  )
})
