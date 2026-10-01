import { randomUUID } from 'node:crypto'
import type { Page } from '@playwright/test'

import { group, png, privateChat, send } from './support/api'
import { expect, signIn, test } from './support/fixtures'
import {
  chatHeader,
  chatRow,
  composer,
  dialog,
  message,
  notice,
  openChat,
  openInfo,
  openSettings,
  search,
  sendText,
} from './support/ui'

const section = (page: Page, heading: string) =>
  page.locator('section.section', { has: page.getByRole('heading', { name: heading }) })

test('a new name and photo reach the people one talks to', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  const chatId = await privateChat(request, alice, bob)
  await send(request, alice, chatId, 'до переименования')
  const a = await open(alice)
  const b = await open(bob)
  await openChat(b, alice.displayName)

  await openSettings(a)
  const profile = section(a, 'Профиль')
  await profile.getByLabel('Имя — его видят другие').fill('Алиса Новая')
  await profile.getByRole('button', { name: 'Сохранить' }).click()
  await expect(chatHeader(b)).toContainText('Алиса Новая')
  await expect(chatRow(b, 'Алиса Новая')).toBeVisible()

  await profile.locator('input[type=file]').setInputFiles({ name: 'me.png', mimeType: 'image/png', buffer: png(300, 300, 2) })
  await expect(profile.getByRole('button', { name: 'Убрать фото' })).toBeVisible({ timeout: 30_000 })
  await expect(chatRow(b, 'Алиса Новая').locator('.avatar img')).toBeVisible()

  await profile.getByRole('button', { name: 'Убрать фото' }).click()
  await expect(chatRow(b, 'Алиса Новая').locator('.avatar img')).toHaveCount(0)

  // An empty name is not accepted.
  await profile.getByLabel('Имя — его видят другие').fill('   ')
  await expect(profile.getByRole('button', { name: 'Сохранить' })).toBeDisabled()

  // A photo that is not an image is refused with a reason.
  await profile.locator('input[type=file]').setInputFiles({ name: 'me.png', mimeType: 'image/png', buffer: Buffer.from('not an image') })
  await expect(profile.locator('.error').or(notice(a, /.+/))).toBeVisible({ timeout: 15_000 })
})

test('changing the password signs other devices out and keeps this one', async ({ open, user, anonymous }) => {
  const alice = await user('alice')
  const desktop = await open(alice)
  const phone = await open(alice)

  await openSettings(desktop)
  const password = section(desktop, 'Пароль')
  const next = randomUUID()
  await password.getByLabel('Текущий пароль').fill('wrong-current')
  await password.getByLabel('Новый пароль', { exact: true }).fill(next)
  await password.getByLabel('Новый пароль ещё раз').fill(next)
  await password.getByRole('button', { name: 'Сменить пароль' }).click()
  await expect(password.locator('.error')).toBeVisible()

  // Mismatched repeat: the button stays off.
  await password.getByLabel('Текущий пароль').fill(alice.password)
  await password.getByLabel('Новый пароль ещё раз').fill(next + 'x')
  await expect(password.getByRole('button', { name: 'Сменить пароль' })).toBeDisabled()

  await password.getByLabel('Новый пароль ещё раз').fill(next)
  await password.getByRole('button', { name: 'Сменить пароль' }).click()
  await expect(password.getByLabel('Текущий пароль')).toHaveValue('')

  await expect(phone).toHaveURL(/\/client\/login/, { timeout: 20_000 })
  await desktop.getByRole('button', { name: '← К чатам' }).click()
  await desktop.reload()
  await expect(desktop).toHaveURL(/\/client\/chat/)

  const old = await anonymous()
  await old.goto('/client/login')
  await old.getByLabel('Логин или email').fill(alice.username)
  await old.getByLabel('Пароль').fill(alice.password)
  await old.getByRole('button', { name: 'Войти' }).click()
  await expect(old.getByRole('alert')).toBeVisible()
  await signIn(old, { ...alice, password: next })
})

test('devices: sign one out, then everywhere', async ({ open, user }) => {
  const alice = await user('alice')
  const desktop = await open(alice)
  const phone = await open(alice, { userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 Mobile/15E148 Safari/604.1' })
  const tablet = await open(alice)

  await openSettings(desktop)
  const devices = section(desktop, 'Устройства')
  // Registration through the API made one more.
  await expect(devices.locator('.device')).toHaveCount(4)
  await expect(devices.locator('.device', { hasText: 'это устройство' })).toHaveCount(1)

  const iphone = devices.locator('.device', { hasText: /iPhone|iOS|Safari/ })
  await iphone.getByRole('button', { name: 'Выйти' }).click()
  await expect(phone).toHaveURL(/\/client\/login/, { timeout: 20_000 })
  await expect(devices.locator('.device')).toHaveCount(3)

  await devices.getByRole('button', { name: 'Выйти на всех устройствах' }).click()
  await dialog(desktop, 'Выйти на всех устройствах?').getByRole('button', { name: 'Выйти везде' }).click()
  await expect(desktop).toHaveURL(/\/client\/login/)
  await expect(tablet).toHaveURL(/\/client\/login/, { timeout: 20_000 })
})

test('blocking: the blocked cannot write, unblocking from settings', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  const chatId = await privateChat(request, alice, bob)
  await send(request, bob, chatId, 'привет до блокировки')
  const a = await open(alice)
  const b = await open(bob)
  await openChat(a, bob.displayName)
  await openChat(b, alice.displayName)

  const info = await openInfo(a)
  await info.getByRole('button', { name: 'Заблокировать' }).click()
  await dialog(a, 'Заблокировать?').getByRole('button', { name: 'Заблокировать' }).click()
  await expect(a.getByText('Вы заблокировали этого пользователя.')).toBeVisible()
  await expect(composer(a)).toHaveCount(0)

  // Bob tries: the message is refused and he is told why (the field gives way to the reason).
  await composer(b).fill('ты меня слышишь?')
  await composer(b).press('Enter')
  await expect(b.getByText('Пользователь ограничил, кто может ему писать.')).toBeVisible({ timeout: 15_000 })
  await expect(message(a, 'ты меня слышишь?')).toHaveCount(0)

  await openSettings(a)
  const blocked = section(a, 'Заблокированные')
  await expect(blocked.locator('.person')).toContainText(bob.displayName)
  await blocked.getByRole('button', { name: 'Разблокировать' }).click()
  await expect(blocked.getByText('Никого')).toBeVisible()

  await b.getByRole('button', { name: 'Попробовать ещё раз' }).click()
  await sendText(b, 'теперь слышишь?')
  await a.getByRole('button', { name: '← К чатам' }).click()
  await openChat(a, bob.displayName)
  await expect(message(a, 'теперь слышишь?')).toBeVisible()
})

test('privacy: who may start a chat and add to groups', async ({ open, user, request }) => {
  const alice = await user('alice')
  const stranger = await user('stranger')
  const friend = await user('friend')
  await privateChat(request, alice, friend)
  const a = await open(alice)

  await openSettings(a)
  const privacy = section(a, 'Приватность')
  await privacy.getByLabel('Кто может начать со мной чат').selectOption('nobody')
  await privacy.getByLabel('Кто может добавлять меня в группы').selectOption('contacts')
  await expect(privacy.locator('select:disabled')).toHaveCount(0)
  await a.reload()
  await expect(privacy.getByLabel('Кто может начать со мной чат')).toHaveValue('nobody')
  await expect(privacy.getByLabel('Кто может добавлять меня в группы')).toHaveValue('contacts')

  // A stranger cannot start a chat.
  const s = await open(stranger)
  await search(s, alice.username)
  await s.locator('.panel .row', { hasText: `@${alice.username}` }).click()
  await expect(notice(s, /ограничил/)).toBeVisible()

  // ...nor add her to a group; a friend can.
  const refused = await request.post('/api/chats/groups', {
    headers: { Authorization: `Bearer ${stranger.token}` },
    data: { title: 'Чужие', memberIds: [alice.userId] },
  })
  expect(refused.ok()).toBe(false)
  await group(request, friend, 'Свои', [alice])
  await a.getByRole('button', { name: '← К чатам' }).click()
  await expect(chatRow(a, 'Свои')).toBeVisible()

  // The existing chat keeps working.
  const f = await open(friend)
  await openChat(f, alice.displayName)
  await sendText(f, 'всё ещё можно')
  await expect(chatRow(a, friend.displayName)).toContainText('всё ещё можно')
})

test('hiding one\'s online status hides it both ways', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  await privateChat(request, alice, bob)
  const a = await open(alice)
  const b = await open(bob)
  await openChat(b, alice.displayName)
  await expect(b.locator('.window .subtitle')).toHaveText('в сети')

  await openSettings(a)
  await section(a, 'Приватность').getByLabel('Кто видит, что я в сети').selectOption('nobody')
  await expect(b.locator('.window .subtitle')).not.toHaveText('в сети', { timeout: 15_000 })
  await b.reload()
  await openChat(b, alice.displayName)
  await expect(b.locator('.window .subtitle')).not.toHaveText('в сети')
})
