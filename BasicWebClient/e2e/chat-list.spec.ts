import type { Page } from '@playwright/test'

import { privateChat, send } from './support/api'
import { expect, test } from './support/fixtures'
import { chatHeader, chatRow, composer, dialog, fromMenu, message, openChat, sendText } from './support/ui'

async function menu(page: Page, title: string, item: string): Promise<void> {
  await chatRow(page, title).click({ button: 'right' })
  await page.getByRole('menu').getByRole('menuitem', { name: item }).click()
}

const titles = (page: Page) => page.locator('.sidebar button.row .title').allInnerTexts()

test('pinning keeps a chat on top, on every device, across reloads', async ({ open, user, request }) => {
  const alice = await user('alice')
  const [bob, carol, dave] = [await user('bob'), await user('carol'), await user('dave')]
  for (const other of [bob, carol, dave]) {
    const chatId = await privateChat(request, alice, other)
    await send(request, other, chatId, `от ${other.displayName}`)
  }
  const a = await open(alice)
  const laptop = await open(alice)

  // The newest is on top; pin the oldest.
  await expect.poll(() => titles(a)).toEqual([dave.displayName, carol.displayName, bob.displayName])
  await menu(a, bob.displayName, 'Закрепить')
  await expect.poll(() => titles(a)).toEqual([`📌${bob.displayName}`, dave.displayName, carol.displayName])
  await expect.poll(() => titles(laptop)).toEqual([`📌${bob.displayName}`, dave.displayName, carol.displayName])

  // A new message elsewhere does not push the pinned chat down.
  const withCarol = await privateChat(request, carol, alice)
  await send(request, carol, withCarol, 'свежее')
  await expect.poll(() => titles(a)).toEqual([`📌${bob.displayName}`, carol.displayName, dave.displayName])

  await a.reload()
  await expect.poll(() => titles(a)).toEqual([`📌${bob.displayName}`, carol.displayName, dave.displayName])
  await menu(a, bob.displayName, 'Открепить')
  await expect(chatRow(laptop, bob.displayName).locator('.pin')).toHaveCount(0)
})

test('archive hides a chat until a new message brings it back', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  const chatId = await privateChat(request, alice, bob)
  await send(request, bob, chatId, 'до архива')
  const a = await open(alice)

  await menu(a, bob.displayName, 'В архив')
  await expect(chatRow(a, bob.displayName)).toHaveCount(0)
  const archive = a.locator('.archive-row')
  await expect(archive).toContainText('Архив')
  await archive.click()
  await expect(chatRow(a, bob.displayName)).toBeVisible()
  await menu(a, bob.displayName, 'Вернуть из архива')
  await a.locator('.heading .back').click()
  await expect(chatRow(a, bob.displayName)).toBeVisible()

  // Archived and unmuted: a new message brings it back to the list.
  await menu(a, bob.displayName, 'В архив')
  await expect(chatRow(a, bob.displayName)).toHaveCount(0)
  await send(request, bob, chatId, 'я вернулся')
  await expect(chatRow(a, bob.displayName)).toContainText('я вернулся')
})

test('muting keeps the count quiet and can be undone', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  const chatId = await privateChat(request, alice, bob)
  const a = await open(alice)
  await expect(chatRow(a, bob.displayName)).toBeVisible()

  await menu(a, bob.displayName, 'Без звука…')
  await a.getByRole('menuitem', { name: 'На 8 часов' }).click()
  await expect(chatRow(a, bob.displayName).getByTitle('Без звука')).toBeVisible()
  await send(request, bob, chatId, 'тихое')
  await expect(chatRow(a, bob.displayName).locator('.badge.quiet')).toHaveText('1')

  await a.reload()
  await expect(chatRow(a, bob.displayName).getByTitle('Без звука')).toBeVisible()
  await menu(a, bob.displayName, 'Включить уведомления')
  await expect(chatRow(a, bob.displayName).getByTitle('Без звука')).toHaveCount(0)
  await expect(chatRow(a, bob.displayName).locator('.badge')).not.toHaveClass(/quiet/)
})

test('marking unread and read by hand', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  const chatId = await privateChat(request, alice, bob)
  await send(request, bob, chatId, 'прочти меня')
  const a = await open(alice)
  const laptop = await open(alice)

  await menu(a, bob.displayName, 'Пометить прочитанным')
  await expect(chatRow(a, bob.displayName).locator('.badge')).toHaveCount(0)
  await expect(chatRow(laptop, bob.displayName).locator('.badge')).toHaveCount(0)

  await menu(a, bob.displayName, 'Пометить непрочитанным')
  await expect(chatRow(a, bob.displayName).locator('.badge.dot')).toBeVisible()
  await expect(chatRow(laptop, bob.displayName).locator('.badge.dot')).toBeVisible()

  // Opening the chat clears the mark.
  await openChat(laptop, bob.displayName)
  await fromMenu(laptop, 'Избранное')
  await expect(chatRow(a, bob.displayName).locator('.badge')).toHaveCount(0)
})

test('folders: create by rule and by hand, edit, delete', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  const carol = await user('carol')
  await privateChat(request, alice, bob)
  await privateChat(request, alice, carol)
  const groupId = (await request
    .post('/api/chats/groups', {
      headers: { Authorization: `Bearer ${alice.token}` },
      data: { title: 'Рабочая группа', memberIds: [bob.userId] },
    })
    .then((r) => r.json())).chatId
  expect(groupId).toBeTruthy()
  const a = await open(alice)
  const laptop = await open(alice)

  await a.getByTitle('Новая папка').click()
  const editor = dialog(a, 'Папка')
  await editor.getByPlaceholder('Название').fill('Группы')
  await editor.getByLabel('Все группы').check()
  await editor.getByRole('button', { name: 'Сохранить' }).click()
  await a.getByRole('navigation', { name: 'Папки' }).getByRole('button', { name: 'Группы' }).click()
  await expect.poll(() => titles(a)).toEqual(['Рабочая группа'])

  // The other device has it too.
  await laptop.getByRole('navigation', { name: 'Папки' }).getByRole('button', { name: 'Группы' }).click()
  await expect.poll(() => titles(laptop)).toEqual(['Рабочая группа'])

  // A hand-picked chat in addition to the rule.
  await a.getByRole('navigation', { name: 'Папки' }).getByRole('button', { name: 'Группы' }).click({ button: 'right' })
  await editor.getByLabel(carol.displayName).check()
  await editor.getByRole('button', { name: 'Сохранить' }).click()
  await expect.poll(async () => (await titles(a)).sort()).toEqual([carol.displayName, 'Рабочая группа'].sort())

  // Deleting the folder returns to all chats.
  await a.getByRole('navigation', { name: 'Папки' }).getByRole('button', { name: 'Группы' }).click({ button: 'right' })
  await editor.getByRole('button', { name: 'Удалить папку' }).click()
  await expect(a.getByRole('navigation', { name: 'Папки' }).getByRole('button', { name: 'Группы' })).toHaveCount(0)
  await expect(laptop.getByRole('navigation', { name: 'Папки' }).getByRole('button', { name: 'Группы' })).toHaveCount(0)
  await expect.poll(async () => (await titles(laptop)).length).toBe(3)
})

test('saved messages: notes to oneself', async ({ open, user }) => {
  const alice = await user('alice')
  const a = await open(alice)
  await fromMenu(a, 'Избранное')
  await expect(chatHeader(a)).toContainText('Избранное')
  await sendText(a, 'купить молоко')
  await expect(message(a, 'купить молоко')).toBeVisible()
  await expect(chatRow(a, 'Избранное')).toContainText('купить молоко')

  // A second click, after a reload, does not make a second chat.
  await a.reload()
  await fromMenu(a, 'Избранное')
  await expect(chatRow(a, 'Избранное')).toHaveCount(1)

  // People search does not offer oneself.
  await a.getByPlaceholder('Поиск чатов, людей и сообщений').fill(alice.username)
  await expect(a.locator('.panel .row', { hasText: `@${alice.username}` })).toHaveCount(0)
  await expect(composer(a)).toBeVisible()
})

test('a chat search matches titles and people', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob', 'Борис Уникальнов')
  await privateChat(request, alice, bob)
  const a = await open(alice)
  await a.getByPlaceholder('Поиск чатов, людей и сообщений').fill('Уникальн')
  await expect(a.locator('.panel', { hasText: 'Найденные чаты' }).locator('.row')).toContainText('Борис Уникальнов')
  await a.getByPlaceholder('Поиск чатов, людей и сообщений').fill('')
  await expect(a.locator('.panel .heading').first()).toHaveText('Чаты')
})
