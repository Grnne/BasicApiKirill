import type { Page } from '@playwright/test'

import { group, png, send } from './support/api'
import { expect, test } from './support/fixtures'
import { chatHeader, chatRow, composer, dialog, message, messageAction, notice, openChat, openInfo, sendText } from './support/ui'

const system = (page: Page, text: string | RegExp) => page.locator('.window .system', { hasText: text })

async function pick(scope: ReturnType<typeof dialog>, username: string): Promise<void> {
  await scope.getByPlaceholder('Имя или логин').fill(username)
  await scope.locator('.found .user', { hasText: `@${username}` }).click()
}

test('creating a group with a photo: members see it at once', async ({ open, user }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  const carol = await user('carol')
  const a = await open(alice)
  const b = await open(bob)
  const c = await open(carol)

  await a.getByTitle('Новая группа').click()
  const create = dialog(a, 'Новая группа')
  await create.locator('input[type=file]').setInputFiles({ name: 'logo.png', mimeType: 'image/png', buffer: png(256, 256, 5) })
  await create.getByLabel('Название').fill('Книжный клуб')
  await pick(create, bob.username)
  await pick(create, carol.username)
  await expect(create.locator('.chips .chip')).toHaveCount(2)
  await create.getByRole('button', { name: 'Создать' }).click()

  await expect(chatHeader(a)).toContainText('Книжный клуб')
  await expect(system(a, 'Создана группа «Книжный клуб»')).toBeVisible()
  for (const page of [b, c]) {
    await expect(chatRow(page, 'Книжный клуб')).toBeVisible()
    await expect(chatRow(page, 'Книжный клуб').locator('.avatar img')).toBeVisible()
  }

  await sendText(a, 'первая встреча в пятницу')
  await openChat(b, 'Книжный клуб')
  await expect(message(b, 'первая встреча в пятницу')).toContainText(alice.displayName)
  await sendText(b, 'буду')
  await expect(chatRow(c, 'Книжный клуб')).toContainText('буду')
  await openChat(c, 'Книжный клуб')
  await expect(message(c, 'первая встреча в пятницу')).toBeVisible()
  await expect(message(c, 'буду')).toBeVisible()
  await expect(chatRow(c, 'Книжный клуб').locator('.badge')).toHaveCount(0)
})

test('adding, removing and leaving: everyone sees what happened', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  const carol = await user('carol')
  const dave = await user('dave')
  const groupId = await group(request, alice, 'Состав', [bob, carol])
  const a = await open(alice)
  const c = await open(carol)
  const d = await open(dave)
  await openChat(a, 'Состав')
  await openChat(c, 'Состав')

  // Add Dave.
  let info = await openInfo(a)
  await info.getByRole('button', { name: '＋ Добавить участников' }).click()
  const add = dialog(a, 'Добавить участников')
  await add.getByPlaceholder('Имя или логин').fill(bob.username)
  await expect(add.locator('.found .user', { hasText: `@${bob.username}` })).toContainText('уже в группе')
  await pick(add, dave.username)
  await add.getByRole('button', { name: 'Добавить' }).click()
  await expect(notice(a, 'Добавлено участников: 1')).toBeVisible()
  await expect(system(c, `Добавлен участник: ${dave.displayName}`)).toBeVisible()
  await expect(chatRow(d, 'Состав')).toBeVisible()
  await expect(info.locator('.member')).toHaveCount(4)

  // Remove Bob: he loses the group, the others see it.
  const b = await open(bob)
  await openChat(b, 'Состав')
  await info.locator('.member', { hasText: bob.displayName }).getByTitle('Исключить').click()
  await dialog(a, 'Исключить из группы?').getByRole('button', { name: 'Исключить' }).click()
  await expect(info.locator('.member')).toHaveCount(3)
  await expect(chatRow(b, 'Состав')).toHaveCount(0)
  await expect(notice(b, 'Вы больше не участник группы «Состав»')).toBeVisible()
  await expect(b.getByText('Выбери чат слева')).toBeVisible()
  await expect(system(c, bob.displayName)).toHaveCount(1)

  // Bob cannot read it any more, even straight from the API.
  const history = await request.get(`/api/chats/${groupId}/messages/cursor`, {
    headers: { Authorization: `Bearer ${bob.token}` },
  })
  expect([403, 404]).toContain(history.status())

  // Carol leaves on her own: no "you are not a member" notice for her.
  info = await openInfo(c)
  await info.getByRole('button', { name: 'Покинуть группу' }).click()
  await dialog(c, 'Покинуть группу?').getByRole('button', { name: 'Покинуть' }).click()
  await expect(chatRow(c, 'Состав')).toHaveCount(0)
  await expect(notice(c, 'Вы больше не участник')).toHaveCount(0)
  await expect(system(a, carol.displayName).last()).toBeVisible()
})

test('roles and rights: a read-only group, a trusted member, a new owner', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  const carol = await user('carol')
  const groupId = await group(request, alice, 'Объявления', [bob, carol])
  const a = await open(alice)
  const b = await open(bob)
  await openChat(a, 'Объявления')
  await openChat(b, 'Объявления')
  await expect(composer(b)).toBeVisible()

  // Members may no longer write.
  let info = await openInfo(a)
  await info.getByRole('button', { name: 'Настройки' }).click()
  const settings = dialog(a, 'Настройки группы')
  await settings.getByLabel('Писать сообщения').uncheck()
  await settings.getByRole('button', { name: 'Сохранить' }).click()
  await expect(b.getByText('В этой группе писать могут только админы')).toBeVisible()
  await expect(composer(b)).toHaveCount(0)

  // ...and the server agrees.
  const refused = await request.post(`/api/chats/${groupId}/messages`, {
    headers: { Authorization: `Bearer ${bob.token}` },
    data: { text: 'в обход' },
  })
  expect(refused.status()).toBe(403)

  // Bob alone gets the right back.
  await info.locator('.member', { hasText: bob.displayName }).getByTitle('Роль и права').click()
  const member = dialog(a, 'Участник')
  await member.getByLabel('Писать сообщения').check()
  await member.getByRole('button', { name: 'Сохранить права' }).click()
  await member.getByRole('button', { name: 'Закрыть' }).click()
  await expect(composer(b)).toBeVisible()
  await sendText(b, 'мне можно')
  await expect(message(a, 'мне можно')).toBeVisible()

  // Bob becomes an admin and can see the audit log.
  await info.locator('.member', { hasText: bob.displayName }).getByTitle('Роль и права').click()
  await member.getByRole('button', { name: 'Сделать админом' }).click()
  await expect(member.locator('.role')).toHaveText('админ')
  await member.getByRole('button', { name: 'Закрыть' }).click()
  info = await openInfo(b)
  await info.getByRole('button', { name: 'Журнал действий' }).click()
  const audit = dialog(b, 'Журнал действий')
  await expect(audit.locator('.entry').first()).toBeVisible()
  await expect(audit).toContainText(bob.displayName)
  await audit.getByTitle('Закрыть').click()

  // Alice hands the group over to Carol.
  const aliceInfo = a.locator('aside.info-panel')
  await aliceInfo.locator('.member', { hasText: carol.displayName }).getByTitle('Роль и права').click()
  await member.getByRole('button', { name: 'Передать группу' }).click()
  await dialog(a, 'Передать группу?').getByRole('button', { name: 'Передать' }).click()
  await member.getByRole('button', { name: 'Закрыть' }).click()
  await expect(aliceInfo.locator('.member', { hasText: carol.displayName }).locator('.role')).toHaveText('владелец')
  await expect(aliceInfo.locator('.member', { hasText: alice.displayName }).locator('.role')).toHaveText('админ')
})

test('a member deletes a message for everyone; an admin deletes another one', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  const id = await group(request, alice, 'Модерация', [bob])
  await send(request, bob, id, 'спам спам спам')
  await send(request, alice, id, 'нормальное')
  const a = await open(alice)
  const b = await open(bob)
  await openChat(a, 'Модерация')
  await openChat(b, 'Модерация')

  await messageAction(a, 'спам спам спам', 'Удалить')
  await dialog(a, 'Удалить сообщение?').getByLabel('Удалить у всех').check()
  await dialog(a, 'Удалить сообщение?').getByRole('button', { name: 'Удалить' }).click()
  await expect(message(b, 'спам спам спам')).toHaveCount(0)

  // Bob is a plain member: Alice's message can only go from his side.
  await messageAction(b, 'нормальное', 'Удалить')
  await expect(dialog(b, 'Удалить сообщение?')).toContainText('только у вас')
})

test('renaming and deleting a group reaches everyone', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  await group(request, alice, 'Старое имя', [bob])
  const a = await open(alice)
  const b = await open(bob)
  await openChat(a, 'Старое имя')
  await openChat(b, 'Старое имя')

  const info = await openInfo(a)
  await info.getByRole('button', { name: 'Настройки' }).click()
  const settings = dialog(a, 'Настройки группы')
  await settings.getByRole('textbox', { name: 'Название' }).fill('Новое имя')
  await settings.getByRole('button', { name: 'Сохранить' }).click()
  await expect(chatHeader(b)).toContainText('Новое имя')
  await expect(system(b, 'Название группы изменено на «Новое имя»')).toBeVisible()

  await info.getByRole('button', { name: 'Настройки' }).click()
  await settings.getByRole('button', { name: 'Удалить группу' }).click()
  await dialog(a, 'Удалить группу?').getByRole('button', { name: 'Удалить' }).click()
  await expect(chatRow(a, 'Новое имя')).toHaveCount(0)
  await expect(chatRow(b, 'Новое имя')).toHaveCount(0)
  await expect(b.getByText('Выбери чат слева')).toBeVisible()
  await expect(notice(a, 'Вы больше не участник')).toHaveCount(0)
})
