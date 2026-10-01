import { expect, type Locator, type Page } from '@playwright/test'

import type { TestUser } from './api'

export const composer = (page: Page): Locator => page.getByPlaceholder('Написать сообщение')

/** A row of the chat list. */
export const chatRow = (page: Page, title: string): Locator =>
  page.locator('.sidebar button.row', { hasText: title })

/** A message (not a system caption) of the open chat, by its own text — not a quote of it in a reply. */
export const message = (page: Page, text: string | RegExp): Locator =>
  page.locator('.window .item[data-message-id]').filter({ has: page.locator('.bubble > .text', { hasText: text }) })

export const chatHeader = (page: Page): Locator => page.locator('.window .head .about')

export async function search(page: Page, query: string): Promise<void> {
  await page.getByPlaceholder('Поиск чатов, людей и сообщений').fill(query)
}

/** Finds a person by login and opens the private chat with them. */
export async function openChatWith(page: Page, user: TestUser): Promise<void> {
  await search(page, user.username)
  await page.locator('.panel .row', { hasText: `@${user.username}` }).click()
  await expect(chatHeader(page)).toContainText(user.displayName)
}

export async function openChat(page: Page, title: string): Promise<void> {
  await chatRow(page, title).first().click()
  await expect(chatHeader(page)).toContainText(title)
}

export async function sendText(page: Page, text: string): Promise<void> {
  await composer(page).fill(text)
  await composer(page).press('Enter')
  await expect(composer(page)).toHaveValue('')
}

/** Picks an action from a message's ⋯ menu. */
export async function messageAction(page: Page, text: string | RegExp, action: string): Promise<void> {
  const item = message(page, text).last()
  await item.hover()
  await item.getByTitle('Действия').click()
  await item.getByRole('menuitem', { name: action }).click()
}

export async function react(page: Page, text: string | RegExp, emoji: string): Promise<void> {
  const item = message(page, text).last()
  await item.hover()
  await item.getByTitle('Действия').click()
  await item.locator('.emoji-row button', { hasText: emoji }).click()
}

export const notice = (page: Page, text: string | RegExp): Locator =>
  page.locator('.notices .notice', { hasText: text })

export async function openSettings(page: Page): Promise<void> {
  await page.getByTitle('Настройки').click()
  await expect(page).toHaveURL(/\/client\/settings/)
}

export async function openInfo(page: Page): Promise<Locator> {
  await chatHeader(page).click()
  const panel = page.locator('aside.info-panel')
  await expect(panel).toBeVisible()
  return panel
}

export const dialog = (page: Page, name: string): Locator => page.getByRole('dialog', { name })
