import { randomUUID } from 'node:crypto'

import { uniqueName } from './support/api'
import { expect, signIn, test } from './support/fixtures'
import { openSettings } from './support/ui'

test('registration through the form signs in and survives a reload', async ({ anonymous }) => {
  const page = await anonymous()
  const username = uniqueName('reg').toLowerCase()

  await page.goto('/client/')
  await expect(page).toHaveURL(/\/client\/login/)
  await page.getByRole('button', { name: 'Регистрация' }).click()
  await page.getByLabel('Логин', { exact: true }).fill(username)
  await page.getByLabel('Email').fill(`${username}@e2e.test`)
  await page.getByLabel('Отображаемое имя').fill('Регина Тестова')
  await page.getByLabel('Пароль').fill(randomUUID())
  await page.getByRole('button', { name: 'Создать аккаунт' }).click()

  await expect(page).toHaveURL(/\/client\/chat/)
  await expect(page.getByTitle('Настройки')).toContainText('Регина Тестова')
  await expect(page.getByText('пока ни одного чата')).toBeVisible()

  await page.reload()
  await expect(page).toHaveURL(/\/client\/chat/)
  await expect(page.locator('.status[data-state="connected"]')).toBeVisible()
})

test('registration errors are shown as text and keep the form', async ({ anonymous, user }) => {
  const taken = await user('taken')
  const page = await anonymous()
  await page.goto('/client/login')
  await page.getByRole('button', { name: 'Регистрация' }).click()

  // The same login again.
  await page.getByLabel('Логин', { exact: true }).fill(taken.username)
  await page.getByLabel('Email').fill(`${uniqueName('other')}@e2e.test`)
  await page.getByLabel('Пароль').fill(randomUUID())
  await page.getByRole('button', { name: 'Создать аккаунт' }).click()
  const error = page.getByRole('alert')
  await expect(error).toBeVisible()
  await expect(error).not.toContainText('{')
  await expect(page).toHaveURL(/\/client\/login/)
  await expect(page.getByLabel('Логин', { exact: true })).toHaveValue(taken.username)

  // Too short a password.
  await page.getByLabel('Логин', { exact: true }).fill(uniqueName('short').toLowerCase())
  await page.getByLabel('Пароль').fill('12345')
  await page.getByRole('button', { name: 'Создать аккаунт' }).click()
  await expect(error).toBeVisible()
  await expect(page).toHaveURL(/\/client\/login/)
})

test('wrong password is refused, the right one lets in', async ({ anonymous, user }) => {
  const alice = await user('alice')
  const page = await anonymous()
  await page.goto('/client/login')
  await page.getByLabel('Логин или email').fill(alice.username)
  await page.getByLabel('Пароль').fill('definitely-wrong')
  await page.getByRole('button', { name: 'Войти' }).click()
  await expect(page.getByRole('alert')).toBeVisible()
  await expect(page).toHaveURL(/\/client\/login/)

  // By email this time.
  await page.getByLabel('Логин или email').fill(alice.email)
  await page.getByLabel('Пароль').fill(alice.password)
  await page.getByRole('button', { name: 'Войти' }).click()
  await expect(page).toHaveURL(/\/client\/chat/)
})

test('a deep link leads through the login back to it', async ({ anonymous, user }) => {
  const alice = await user('alice')
  const page = await anonymous()
  await page.goto('/client/settings')
  await expect(page).toHaveURL(/\/client\/login\?redirect=/)
  await page.getByLabel('Логин или email').fill(alice.username)
  await page.getByLabel('Пароль').fill(alice.password)
  await page.getByRole('button', { name: 'Войти' }).click()
  await expect(page).toHaveURL(/\/client\/settings/)
})

test('logout returns to the login and the old session is gone', async ({ open, user }) => {
  const alice = await user('alice')
  const page = await open(alice)
  await page.getByRole('button', { name: 'Выйти' }).click()
  await expect(page).toHaveURL(/\/client\/login/)
  expect(await page.evaluate(() => localStorage.getItem('basicchat.refreshToken'))).toBeNull()

  await page.goto('/client/chat')
  await expect(page).toHaveURL(/\/client\/login/)
})

test('a markup-looking name is shown as text, not run', async ({ open, user }) => {
  const evil = await user('evil', '<img src=x onerror="window.__pwned=1">')
  const alice = await user('alice')
  const page = await open(evil)
  await expect(page.getByTitle('Настройки')).toContainText('<img src=x')
  expect(await page.evaluate(() => (window as any).__pwned)).toBeUndefined()

  const other = await open(alice)
  await other.getByPlaceholder('Поиск чатов, людей и сообщений').fill(evil.username)
  await expect(other.locator('.panel .row', { hasText: '<img src=x' })).toBeVisible()
  expect(await other.evaluate(() => (window as any).__pwned)).toBeUndefined()
})

test('many tabs of one browser share the session without logging each other out', async ({ open, user }) => {
  const alice = await user('alice')
  const first = await open(alice)
  const context = first.context()
  const tabs = [first, await context.newPage(), await context.newPage()]

  // Every reload refreshes the token, and every refresh rotates it: tabs race for it.
  for (let round = 0; round < 3; round++) {
    await Promise.all(tabs.map((tab) => tab.goto('/client/chat')))
    for (const tab of tabs) {
      await expect(tab).toHaveURL(/\/client\/chat/)
      await expect(tab.locator('.status[data-state="connected"]')).toBeVisible()
    }
  }
  await openSettings(tabs[1]!)
  await expect(tabs[1]!.getByText(`@${alice.username}`)).toBeVisible()
})

test('signing in again after a logout elsewhere works', async ({ open, user, anonymous }) => {
  const alice = await user('alice')
  const page = await open(alice)
  await page.getByRole('button', { name: 'Выйти' }).click()
  const again = await anonymous()
  await signIn(again, alice)
})
