import { api, group, privateChat, send } from './support/api'
import { expect, test } from './support/fixtures'
import {
  chatHeader,
  chatRow,
  composer,
  dialog,
  message,
  messageAction,
  openChat,
  openChatWith,
  react,
  search,
  sendText,
} from './support/ui'

test('a first message reaches the other side live, and the read mark comes back', async ({ open, user }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  const a = await open(alice)
  const b = await open(bob)

  await openChatWith(a, bob)
  await sendText(a, 'Привет, Боб!')
  await expect(message(a, 'Привет, Боб!').locator('.status')).toHaveAttribute('title', /Отправлено|Доставлено/)

  // The chat appears in Bob's list without a reload, unread.
  const row = chatRow(b, alice.displayName)
  await expect(row).toContainText('Привет, Боб!')
  await expect(row.locator('.badge')).toHaveText('1')

  await row.click()
  await expect(message(b, 'Привет, Боб!')).toBeVisible()
  await expect(row.locator('.badge')).toHaveCount(0)
  await expect(message(a, 'Привет, Боб!').locator('.status')).toHaveAttribute('title', 'Прочитано')

  // And back.
  await sendText(b, 'Привет, Алиса')
  await expect(message(a, 'Привет, Алиса')).toBeVisible()
})

test('typing shows on the other side', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  await privateChat(request, alice, bob)
  const a = await open(alice)
  const b = await open(bob)
  await openChat(a, bob.displayName)
  await openChat(b, alice.displayName)

  await composer(a).pressSequentially('пишу длинный ответ', { delay: 30 })
  await expect(b.locator('.window .subtitle')).toHaveText('печатает…')
  await expect(chatRow(b, alice.displayName)).not.toContainText('печатает…') // the open chat's row is the header
  await composer(a).fill('')
  await expect(b.locator('.window .subtitle')).not.toHaveText('печатает…', { timeout: 15_000 })
})

test('reply, edit, reaction and deletion show on both sides', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  const chatId = await privateChat(request, alice, bob)
  await send(request, bob, chatId, 'Исходное сообщение')
  await send(request, bob, chatId, 'Второе от Боба')
  const a = await open(alice)
  const b = await open(bob)
  await openChat(a, bob.displayName)
  await openChat(b, alice.displayName)

  // Reply.
  await messageAction(a, 'Исходное сообщение', 'Ответить')
  await expect(a.locator('.composer .context')).toContainText('Ответ')
  await sendText(a, 'Мой ответ')
  const replied = message(b, 'Мой ответ')
  await expect(replied.locator('.reply')).toContainText('Исходное сообщение')

  // Edit.
  await messageAction(a, 'Мой ответ', 'Изменить')
  await expect(composer(a)).toHaveValue('Мой ответ')
  await composer(a).fill('Мой исправленный ответ')
  await composer(a).press('Enter')
  await expect(message(b, 'Мой исправленный ответ')).toContainText('изменено')
  await expect(message(b, /^Мой ответ/)).toHaveCount(0)

  // Reaction, then taking it back.
  await react(b, 'Мой исправленный ответ', '👍')
  await expect(message(a, 'Мой исправленный ответ').locator('.reaction')).toContainText('👍 1')
  await message(b, 'Мой исправленный ответ').locator('.reaction.mine').click()
  await expect(message(a, 'Мой исправленный ответ').locator('.reaction')).toHaveCount(0)

  // Deleting for everyone: gone on both sides, the reply now says the original is deleted.
  await messageAction(b, 'Исходное сообщение', 'Удалить')
  const confirm = dialog(b, 'Удалить сообщение?')
  await confirm.getByLabel('Удалить у всех').check()
  await confirm.getByRole('button', { name: 'Удалить' }).click()
  await expect(message(a, 'Исходное сообщение')).toHaveCount(0)
  await expect(message(b, 'Исходное сообщение')).toHaveCount(0)
  await expect(message(a, 'Мой исправленный ответ').locator('.reply')).toContainText('Сообщение удалено')

  // Deleting only for me: the other side keeps it.
  await messageAction(a, 'Второе от Боба', 'Удалить')
  await expect(dialog(a, 'Удалить сообщение?')).toContainText('только у вас')
  await dialog(a, 'Удалить сообщение?').getByRole('button', { name: 'Удалить' }).click()
  await expect(message(a, 'Второе от Боба')).toHaveCount(0)
  await a.reload()
  await openChat(a, bob.displayName)
  await expect(message(a, 'Мой исправленный ответ')).toBeVisible()
  await expect(message(a, 'Второе от Боба')).toHaveCount(0)
  await expect(message(b, 'Второе от Боба')).toBeVisible()
})

test('formatting and links survive the trip; unsafe links never become links', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  const chatId = await privateChat(request, alice, bob)
  const a = await open(alice)
  const b = await open(bob)
  await openChat(a, bob.displayName)
  await openChat(b, alice.displayName)

  const text = 'жирный и ссылка и опасная'
  const select = (from: number, to: number) =>
    composer(a).evaluate((el: HTMLTextAreaElement, [s, e]) => {
      el.focus()
      el.setSelectionRange(s!, e!)
      el.dispatchEvent(new Event('select'))
    }, [from, to])

  await composer(a).fill(text)
  await select(0, 6)
  await composer(a).press('Control+b')
  await select(9, 15)
  await a.getByTitle('Ссылка (Ctrl+K)').click()
  await a.getByPlaceholder('https://… (пусто — убрать ссылку)').fill('https://example.com/путь?q=1')
  await a.getByPlaceholder('https://… (пусто — убрать ссылку)').press('Enter')
  await select(18, 25)
  await a.getByTitle('Ссылка (Ctrl+K)').click()
  await a.getByPlaceholder('https://… (пусто — убрать ссылку)').fill('javascript:alert(1)')
  await a.getByPlaceholder('https://… (пусто — убрать ссылку)').press('Enter')
  await composer(a).press('Enter')

  const got = message(b, 'жирный и ссылка')
  await expect(got.locator('strong')).toHaveText('жирный')
  const link = got.locator('a')
  await expect(link).toHaveCount(1)
  await expect(link).toHaveText('ссылка')
  await expect(link).toHaveAttribute('href', /^https:\/\/example\.com\//)
  await expect(link).toHaveAttribute('rel', /noopener/)
  await expect(link).toHaveAttribute('target', '_blank')

  // The server refuses an unsafe link even from a client that skips the checks.
  const response = await request.post(`/api/chats/${chatId}/messages`, {
    headers: { Authorization: `Bearer ${alice.token}` },
    data: { text: 'click', entities: [{ type: 'link', offset: 0, length: 5, url: 'javascript:alert(1)' }] },
  })
  expect(response.status()).toBe(400)
})

test('markup in a message is text', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  await privateChat(request, alice, bob)
  const a = await open(alice)
  const b = await open(bob)
  await openChat(a, bob.displayName)
  await openChat(b, alice.displayName)

  const evil = '<script>window.__pwned=1</script><b>не жирный</b><img src=x onerror="window.__pwned=2">'
  await sendText(a, evil)
  const got = message(b, '<script>')
  await expect(got.locator('.text')).toHaveText(evil)
  await expect(got.locator('b, img, script')).toHaveCount(0)
  expect(await b.evaluate(() => (window as any).__pwned)).toBeUndefined()
})

test('lines, emoji, right-to-left and the longest text arrive intact', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  await privateChat(request, alice, bob)
  const config = await api(request, alice, 'GET', '/api/config')
  const maxLength: number = config.messages.maxLength
  const a = await open(alice)
  const b = await open(bob)
  await openChat(a, bob.displayName)
  await openChat(b, alice.displayName)

  await composer(a).fill('первая строка')
  await composer(a).press('Shift+Enter')
  await composer(a).pressSequentially('вторая 👨‍👩‍👧 🇷🇺 שלום עולם')
  await composer(a).press('Enter')
  await expect(message(b, 'первая строка').locator('.text')).toHaveText('первая строка\nвторая 👨‍👩‍👧 🇷🇺 שלום עולם')

  const longest = 'я'.repeat(maxLength - 5) + 'конец'
  await composer(a).fill(longest + 'лишнее')
  expect((await composer(a).inputValue()).length).toBeLessThanOrEqual(maxLength)
  await composer(a).fill(longest)
  await composer(a).press('Enter')
  const got = message(b, 'яконец')
  await expect(got).toBeVisible()
  expect((await got.locator('.text').innerText()).length).toBe(maxLength)
})

test('a burst of messages arrives complete, once each and in order', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  await privateChat(request, alice, bob)
  const a = await open(alice)
  const b = await open(bob)
  await openChat(a, bob.displayName)
  await openChat(b, alice.displayName)

  const texts = Array.from({ length: 15 }, (_, i) => `очередь ${String(i + 1).padStart(2, '0')}`)
  for (const text of texts) {
    await composer(a).fill(text)
    await composer(a).press('Enter')
  }
  await expect(b.locator('.window .item .text', { hasText: 'очередь' })).toHaveCount(15)
  expect(await b.locator('.window .item .text', { hasText: 'очередь' }).allInnerTexts()).toEqual(texts)
  await expect(a.locator('.window .bubble.sending, .window .bubble.failed')).toHaveCount(0)
  expect(await a.locator('.window .item .text', { hasText: 'очередь' }).allInnerTexts()).toEqual(texts)
})

test('past the send limit nothing is lost or doubled', async ({ open, user, request }) => {
  test.setTimeout(120_000)
  const alice = await user('alice')
  const bob = await user('bob')
  await privateChat(request, alice, bob)
  const a = await open(alice)
  const b = await open(bob)
  await openChat(a, bob.displayName)
  await openChat(b, alice.displayName)

  // The server takes 20 commands per 10 s from a user.
  const texts = Array.from({ length: 30 }, (_, i) => `лавина ${String(i + 1).padStart(2, '0')}`)
  for (const text of texts) {
    await composer(a).fill(text)
    await composer(a).press('Enter')
  }

  // Whatever the client could not send waits with a retry button.
  await expect(a.locator('.window .bubble.sending')).toHaveCount(0, { timeout: 40_000 })
  const failed = a.locator('.window .bubble.failed')
  while ((await failed.count()) > 0) {
    await failed.first().getByRole('button', { name: 'Повторить' }).click()
    await expect(a.locator('.window .bubble.sending')).toHaveCount(0, { timeout: 30_000 })
  }

  const got = b.locator('.window .item .text', { hasText: 'лавина' })
  await expect(got).toHaveCount(30, { timeout: 30_000 })
  expect(new Set(await got.allInnerTexts())).toEqual(new Set(texts))
  await b.reload()
  await openChat(b, alice.displayName)
  await expect(b.locator('.window .item .text', { hasText: 'лавина' })).toHaveCount(30)
})

test('a message written offline goes out once the network is back', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  await privateChat(request, alice, bob)
  const a = await open(alice)
  const b = await open(bob)
  await openChat(a, bob.displayName)
  await openChat(b, alice.displayName)

  await a.context().setOffline(true)
  await sendText(a, 'написано без сети')
  await expect(a.locator('.window .bubble.sending')).toContainText('написано без сети')
  await a.waitForTimeout(2_000)
  await a.context().setOffline(false)

  await expect(message(b, 'написано без сети')).toHaveCount(1, { timeout: 30_000 })
  await expect(a.locator('.window .bubble.sending, .window .bubble.failed')).toHaveCount(0, { timeout: 30_000 })
  await expect(message(a, 'написано без сети')).toHaveCount(1)
  await expect(a.locator('.status[data-state="connected"]')).toBeVisible({ timeout: 30_000 })

  // A long outage: the send gives up, says so, and a retry delivers it once.
  await a.context().setOffline(true)
  await sendText(a, 'долгий обрыв')
  const failed = a.locator('.window .bubble.failed')
  await expect(failed).toContainText('Не отправлено', { timeout: 30_000 })
  await a.context().setOffline(false)
  await failed.getByRole('button', { name: 'Повторить' }).click()
  await expect(message(b, 'долгий обрыв')).toHaveCount(1, { timeout: 30_000 })
  await expect(message(a, 'долгий обрыв')).toHaveCount(1)
})

test('long history loads page by page to its start, in order', async ({ open, user, request }) => {
  test.setTimeout(120_000)
  const alice = await user('alice')
  const senders = [await user('bob'), await user('carol'), await user('dave')]
  const chatId = await group(request, alice, 'Длинная история', senders)
  const texts: string[] = []
  for (let i = 1; i <= 60; i++) {
    const text = `история ${String(i).padStart(3, '0')}`
    texts.push(text)
    await send(request, senders[i % 3]!, chatId, text)
  }

  const a = await open(alice)
  await openChat(a, 'Длинная история')
  const shown = a.locator('.window .item .text', { hasText: 'история' })
  await expect(shown.last()).toHaveText('история 060')

  const viewport = a.locator('.window .viewport')
  for (let i = 0; i < 10 && !(await a.getByText('начало переписки').isVisible()); i++) {
    await viewport.evaluate((el) => el.scrollTo(0, 0))
    await a.waitForTimeout(400)
  }
  await expect(a.getByText('начало переписки')).toBeVisible()
  expect(await shown.allInnerTexts()).toEqual(texts)
})

test('a draft follows across chats, reloads and devices', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  await privateChat(request, alice, bob)
  const a = await open(alice)
  await openChat(a, bob.displayName)
  await composer(a).fill('недописанная мысль')

  await a.getByTitle('Избранное').click()
  await expect(chatHeader(a)).toContainText('Избранное')
  await expect(chatRow(a, bob.displayName)).toContainText('Черновик: недописанная мысль')

  const laptop = await open(alice)
  await expect(chatRow(laptop, bob.displayName)).toContainText('Черновик: недописанная мысль')

  await a.reload()
  await openChat(a, bob.displayName)
  await expect(composer(a)).toHaveValue('недописанная мысль')

  // Sent — the draft is gone everywhere.
  await composer(a).press('Enter')
  await expect(chatRow(laptop, bob.displayName)).not.toContainText('Черновик', { timeout: 15_000 })
  await expect(chatRow(laptop, bob.displayName)).toContainText('недописанная мысль')
})

test('search finds messages in the chat and across chats and jumps to them', async ({ open, user, request }) => {
  test.setTimeout(90_000)
  const alice = await user('alice')
  const bob = await user('bob')
  const carol = await user('carol')
  const withBob = await privateChat(request, alice, bob)
  const withCarol = await privateChat(request, alice, carol)
  await send(request, bob, withBob, 'где ключ от подвала?')
  for (let i = 0; i < 40; i++) await send(request, i % 2 ? bob : alice, withBob, `болтовня ${i}`)
  await send(request, carol, withCarol, 'ключ лежит под ковриком')

  const a = await open(alice)
  await search(a, 'ключ')
  const hits = a.locator('.panel .hit')
  await expect(hits).toHaveCount(2)
  await hits.filter({ hasText: 'подвала' }).click()
  await expect(chatHeader(a)).toContainText(bob.displayName)
  await expect(message(a, 'где ключ от подвала?')).toBeInViewport()

  // In-chat search: only this chat's messages.
  await a.getByTitle('Поиск в чате').click()
  await a.getByPlaceholder('Поиск в чате').fill('ключ')
  await expect(a.getByText('Найдено: 1')).toBeVisible()
  await a.getByPlaceholder('Поиск в чате').fill('болтовня 3')
  await expect(a.locator('.search .hit').first()).toBeVisible()

  await search(a, 'нет-такого-слова-нигде')
  await expect(a.locator('.panel', { hasText: 'Сообщения' }).getByText('ничего не нашлось')).toBeVisible()
})

test('forwarding one message and a selection', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  const carol = await user('carol')
  const withBob = await privateChat(request, alice, bob)
  await privateChat(request, alice, carol)
  await send(request, bob, withBob, 'рецепт пирога')
  await send(request, bob, withBob, 'и ещё про крем')
  const a = await open(alice)
  const c = await open(carol)
  await openChat(a, bob.displayName)

  await messageAction(a, 'рецепт пирога', 'Переслать')
  await dialog(a, 'Переслать').getByRole('button', { name: '★ Избранное' }).click()
  await a.getByTitle('Избранное').click()
  await expect(message(a, 'рецепт пирога')).toContainText(`Переслано от ${bob.displayName}`)

  await openChat(a, bob.displayName)
  await messageAction(a, 'рецепт пирога', 'Выбрать')
  await message(a, 'и ещё про крем').click()
  await expect(a.getByText('Выбрано: 2')).toBeVisible()
  await a.locator('.bar').getByRole('button', { name: 'Переслать' }).click()
  await dialog(a, 'Переслать').getByPlaceholder('Найти чат').fill(carol.displayName)
  await dialog(a, 'Переслать').getByRole('button', { name: carol.displayName }).click()

  await openChat(c, alice.displayName)
  await expect(message(c, 'рецепт пирога')).toContainText(`Переслано от ${bob.displayName}`)
  await expect(message(c, 'и ещё про крем')).toBeVisible()
})

test('reading on one device clears the unread count on another', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  const chatId = await privateChat(request, alice, bob)
  const phone = await open(alice)
  const laptop = await open(alice)
  await send(request, bob, chatId, 'раз')
  await send(request, bob, chatId, 'два')

  await expect(chatRow(laptop, bob.displayName).locator('.badge')).toHaveText('2')
  await openChat(phone, bob.displayName)
  await expect(chatRow(laptop, bob.displayName).locator('.badge')).toHaveCount(0)
})

test('a mention suggests members and marks the chat for the mentioned', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  const carol = await user('carol')
  await group(request, alice, 'Упоминания', [bob, carol])
  const a = await open(alice)
  const b = await open(bob)
  await openChat(a, 'Упоминания')

  await composer(a).pressSequentially(`вопрос к @${bob.username.slice(0, 5)}`)
  await a.getByRole('listbox').getByRole('button', { name: new RegExp(bob.displayName) }).click()
  await composer(a).pressSequentially('посмотри')
  await composer(a).press('Enter')

  await expect(chatRow(b, 'Упоминания').locator('.badge.mention')).toBeVisible()
  await openChat(b, 'Упоминания')
  await expect(message(b, 'вопрос к').locator('.mention, [class*="mention"]').first()).toBeVisible()
  await expect(chatRow(b, 'Упоминания').locator('.badge.mention')).toHaveCount(0)
})
