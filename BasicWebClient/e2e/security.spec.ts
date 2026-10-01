import { group, privateChat, send } from './support/api'
import { expect, test } from './support/fixtures'

test('the app is served with protective headers', async ({ request }) => {
  const response = await request.get('/client/')
  expect(response.status()).toBe(200)
  const h = response.headers()
  expect(h['content-security-policy']).toMatch(/default-src 'self'/)
  expect(h['content-security-policy']).toMatch(/frame-ancestors 'none'/)
  expect(h['content-security-policy']).not.toMatch(/unsafe-eval/)
  expect(h['x-content-type-options']).toBe('nosniff')
  expect(h['referrer-policy']).toBeTruthy()
  expect(h['strict-transport-security']).toMatch(/max-age=\d+/)
  expect(h['server'] ?? '').not.toMatch(/kestrel/i)

  expect((await request.get('/swagger/index.html')).status()).toBe(404)
})

test('only the token to renew the session is stored in the browser', async ({ open, user }) => {
  const alice = await user('alice')
  const page = await open(alice)
  const stored = await page.evaluate(() => Object.keys(localStorage).concat(Object.keys(sessionStorage)))
  expect(stored.filter((k) => !k.startsWith('basicchat.') || /access|jwt/i.test(k))).toEqual([])
  const cookies = await page.context().cookies()
  expect(cookies).toEqual([])
})

test('a stranger cannot read, write or change a chat they are not in', async ({ user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  const mallory = await user('mallory')
  const chatId = await privateChat(request, alice, bob)
  const { id: messageId } = await send(request, bob, chatId, 'секрет')
  const groupId = await group(request, alice, 'Закрытая', [bob])
  const as = { Authorization: `Bearer ${mallory.token}` }

  const attempts = [
    request.get(`/api/chats/${chatId}`, { headers: as }),
    request.get(`/api/chats/${chatId}/messages/cursor`, { headers: as }),
    request.get(`/api/chats/${chatId}/messages/search?q=секрет`, { headers: as }),
    request.get(`/api/chats/${chatId}/media?filter=media`, { headers: as }),
    request.post(`/api/chats/${chatId}/messages`, { headers: as, data: { text: 'я тут' } }),
    request.patch(`/api/chats/${chatId}/messages/${messageId}`, { headers: as, data: { text: 'подмена' } }),
    request.delete(`/api/chats/${chatId}/messages/${messageId}?forEveryone=true`, { headers: as }),
    request.put(`/api/chats/${chatId}/messages/${messageId}/reactions`, { headers: as, data: { emoji: '👍' } }),
    request.post(`/api/chats/${chatId}/read`, { headers: as, data: { lastMessageId: messageId } }),
    request.post(`/api/chats/${groupId}/members`, { headers: as, data: { userIds: [alice.userId] } }),
    request.patch(`/api/chats/${groupId}`, { headers: as, data: { title: 'захвачено' } }),
    request.delete(`/api/chats/${groupId}`, { headers: as }),
    request.get(`/api/chats/${groupId}/audit`, { headers: as }),
  ]
  // Each probe is a well-formed request: refused for who sends it, not for its shape (a 400
  // would pass without the membership check ever running).
  for (const response of await Promise.all(attempts)) {
    expect([403, 404], `${response.url()} → ${response.status()}`).toContain(response.status())
  }

  // Nothing leaked into the search across chats either.
  const found = await request.get('/api/search/messages?q=секрет', { headers: as })
  expect((await found.json()).items).toEqual([])
})

test('a stranger\'s chat id in a link opens nothing', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  const mallory = await user('mallory')
  const chatId = await privateChat(request, alice, bob)
  const page = await open(mallory)
  await page.goto(`/client/chat?open=${chatId}`)
  await expect(page.getByText('Выбери чат слева')).toBeVisible()
  await expect(page).toHaveURL(/\/client\/chat$/)
})

test('a renewal token works once; after logout it is dead', async ({ user, request }) => {
  test.setTimeout(90_000)
  const alice = await user('alice')
  const login = await (await request.post('/api/auth/login', {
    data: { usernameOrEmail: alice.username, password: alice.password },
  })).json()

  const first = await request.post('/api/auth/refresh', { data: { refreshToken: login.refreshToken } })
  expect(first.status()).toBe(200)
  const renewed = await first.json()
  // Replayed after the grace window for racing tabs (30 s): theft, the whole chain dies.
  await new Promise((r) => setTimeout(r, 31_000))
  expect((await request.post('/api/auth/refresh', { data: { refreshToken: login.refreshToken } })).status()).toBe(401)
  expect((await request.post('/api/auth/refresh', { data: { refreshToken: renewed.refreshToken } })).status()).toBe(401)

  const fresh = await (await request.post('/api/auth/login', {
    data: { usernameOrEmail: alice.username, password: alice.password },
  })).json()
  await request.post('/api/auth/logout', {
    headers: { Authorization: `Bearer ${fresh.token}` },
    data: { refreshToken: fresh.refreshToken },
  })
  expect((await request.post('/api/auth/refresh', { data: { refreshToken: fresh.refreshToken } })).status()).toBe(401)
  expect(renewed.token).toBeTruthy()
})

test('oversized and malformed input is refused cleanly', async ({ user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  const chatId = await privateChat(request, alice, bob)
  const as = { Authorization: `Bearer ${alice.token}` }
  const config = await (await request.get('/api/config', { headers: as })).json()

  const tooLong = await request.post(`/api/chats/${chatId}/messages`, {
    headers: as,
    data: { text: 'x'.repeat(config.messages.maxLength + 1) },
  })
  expect(tooLong.status()).toBe(400)
  expect((await tooLong.json()).errorCode).toBeTruthy()

  const badEntity = await request.post(`/api/chats/${chatId}/messages`, {
    headers: as,
    data: { text: 'abc', entities: [{ type: 'bold', offset: 2, length: 50 }] },
  })
  expect(badEntity.status()).toBe(400)

  const notJson = await request.post(`/api/chats/${chatId}/messages`, {
    headers: { ...as, 'Content-Type': 'application/json' },
    data: '{"text":',
  })
  expect(notJson.status()).toBe(400)

  // Over the API's body limit: refused with a reason, not a server error.
  const big = await request.post(`/api/chats/${chatId}/messages`, {
    headers: { ...as, 'Content-Type': 'application/json' },
    data: JSON.stringify({ text: 'y'.repeat(100 * 1024) }),
  })
  expect(big.status()).toBe(413)
  expect((await big.json()).errorCode).toBe('REQUEST_TOO_LARGE')

  // Far over it: cut off early (the proxy may only see the connection drop), and the API lives on.
  const huge = await request.post(`/api/chats/${chatId}/messages`, {
    headers: { ...as, 'Content-Type': 'application/json' },
    data: JSON.stringify({ text: 'y'.repeat(30 * 1024 * 1024) }),
  }).catch((e) => e)
  if (!(huge instanceof Error)) expect([413, 502]).toContain(huge.status())
  expect((await request.get('/api/chats', { headers: as })).status()).toBe(200)

  const badId = await request.get('/api/chats/not-a-guid/messages/cursor', { headers: as })
  expect([400, 404]).toContain(badId.status())
})

test('the hub refuses a connection without a valid token', async ({ open, user }) => {
  const alice = await user('alice')
  const page = await open(alice)
  const result = await page.evaluate(async () => {
    const tryOpen = (url: string) =>
      new Promise<string>((resolve) => {
        const ws = new WebSocket(url)
        ws.onopen = () => {
          ws.send('{"protocol":"json","version":1}\x1e')
        }
        ws.onmessage = (e) => resolve(`message:${String(e.data).slice(0, 60)}`)
        ws.onclose = (e) => resolve(`close:${e.code}`)
        setTimeout(() => resolve('timeout'), 5000)
      })
    const base = location.origin.replace(/^http/, 'ws')
    return [await tryOpen(`${base}/hubs/chat`), await tryOpen(`${base}/hubs/chat?access_token=forged.token.value`)]
  })
  // Refused at the handshake: the socket closes. A handshake answer ("{}") would mean it was let
  // in, and a timeout would hide either.
  for (const r of result) expect(r).toMatch(/^close:/)
  // The page's own connection is not affected.
  await expect(page.getByText('Выбери чат слева')).toBeVisible()
  await expect(page.getByText('нет связи')).toHaveCount(0)
})
