// Service worker of the web client: shows push notifications about new messages and opens the
// chat on a click. It holds no access token: everything shown comes in the notification itself
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

self.addEventListener('push', (event) => {
  let payload = null
  try {
    payload = event.data ? event.data.json() : null
  } catch {
    payload = null
  }
  // Only `message` exists so far; an unknown kind is skipped.
  if (!payload || payload.kind !== 'message' || !payload.chatId) return

  const group = payload.chatType === 'group'
  const title = group ? payload.chatTitle || 'Группа' : payload.senderName || 'Новое сообщение'
  const body = group && payload.messageType !== 'system' ? `${payload.senderName}: ${bodyOf(payload)}` : bodyOf(payload)

  event.waitUntil(
    self.registration.showNotification(title, {
      body,
      // One notification per chat: a newer one replaces the older.
      tag: `chat-${payload.chatId}`,
      renotify: true,
      data: { chatId: payload.chatId },
    }),
  )
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
