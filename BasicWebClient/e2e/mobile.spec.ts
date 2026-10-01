import type { Page } from '@playwright/test'

import { privateChat, send } from './support/api'
import { expect, test } from './support/fixtures'
import { chatHeader, chatRow, composer, message, messageAction, openInfo, openSettings, sendText } from './support/ui'

const phone = { viewport: { width: 375, height: 740 }, isMobile: true, hasTouch: true }

/** A phone lays out a page wider than itself and shrinks or scrolls it: the layout must fit. */
async function noSideScroll(page: Page): Promise<void> {
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(phone.viewport.width)
}

test('on a phone: list, chat and back, info, settings — nothing spills sideways', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob', 'Бартоломей Длиннофамильный-Тестировщиков')
  const chatId = await privateChat(request, alice, bob)
  await send(request, bob, chatId, 'оченьдлинноесловобезпробеловкотороедолжнопереноситьсяанеломатьвёрсткунателефоне')
  const a = await open(alice, phone)

  await expect(chatRow(a, bob.displayName)).toBeVisible()
  await noSideScroll(a)

  await chatRow(a, bob.displayName).click()
  await expect(chatHeader(a)).toBeVisible()
  await expect(chatRow(a, bob.displayName)).toBeHidden()
  await expect(message(a, 'оченьдлинноеслово')).toBeVisible()
  await noSideScroll(a)

  // The menu of an incoming message, whose bubble fills the width: it stays on the screen. The
  // page's own scroll width cannot show it — the list clips and scrolls inside itself.
  await message(a, 'оченьдлинноеслово').hover()
  await message(a, 'оченьдлинноеслово').locator('button.more').click()
  const menu = a.locator('.window .menu')
  await expect(menu).toBeVisible()
  const box = (await menu.boundingBox())!
  expect(box.x).toBeGreaterThanOrEqual(0)
  expect(box.x + box.width).toBeLessThanOrEqual(phone.viewport.width)
  await menu.press('Escape')
  await expect(menu).toBeHidden()

  await sendText(a, 'с телефона')
  await expect(message(a, 'с телефона')).toBeVisible()
  await messageAction(a, 'с телефона', 'Изменить')
  await expect(composer(a)).toHaveValue('с телефона')
  await composer(a).press('Escape')

  const info = await openInfo(a)
  await expect(info).toBeInViewport()
  await info.getByTitle('Закрыть').click()

  await a.getByTitle('К списку чатов').click()
  await expect(chatRow(a, bob.displayName)).toBeVisible()

  await openSettings(a)
  await noSideScroll(a)
  await a.getByRole('button', { name: '← К чатам' }).click()
  await expect(chatRow(a, bob.displayName)).toBeVisible()
})
