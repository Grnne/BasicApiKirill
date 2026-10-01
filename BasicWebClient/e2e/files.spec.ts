import { randomBytes } from 'node:crypto'
import { writeFileSync } from 'node:fs'
import type { Locator, Page } from '@playwright/test'

import { png, privateChat } from './support/api'
import { expect, test } from './support/fixtures'
import { composer, dialog, message, notice, openChat, openInfo } from './support/ui'

const fileInput = (page: Page) => page.locator('.composer input[type=file]')
const sendButton = (page: Page) => page.locator('.composer button.send')

async function loaded(img: Locator): Promise<void> {
  await expect(img).toBeVisible()
  await expect.poll(() => img.evaluate((el: HTMLImageElement) => el.complete && el.naturalWidth > 0)).toBe(true)
}

test('a photo album with a caption: thumbnails, viewer, gallery', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  await privateChat(request, alice, bob)
  const a = await open(alice)
  const b = await open(bob)
  await openChat(a, bob.displayName)
  await openChat(b, alice.displayName)

  await fileInput(a).setInputFiles([0, 1, 2].map((i) => ({ name: `фото-${i}.png`, mimeType: 'image/png', buffer: png(640, 400, i) })))
  await expect(a.locator('.tray .item')).toHaveCount(3)
  await expect(sendButton(a)).toBeEnabled({ timeout: 30_000 })
  await composer(a).fill('отпуск')
  await sendButton(a).click()

  const album = message(b, 'отпуск')
  await expect(album.locator('.tile')).toHaveCount(3)
  for (const img of await album.locator('.tile img').all()) await loaded(img)

  await album.locator('.tile').nth(1).click()
  const viewer = dialog(b, 'Просмотр')
  await expect(viewer.locator('.counter')).toHaveText('2 / 3')
  await loaded(viewer.locator('img.media'))
  await viewer.getByTitle('Следующее').click()
  await expect(viewer.locator('.counter')).toHaveText('3 / 3')
  await b.keyboard.press('Escape')
  await expect(viewer).toHaveCount(0)

  const info = await openInfo(b)
  await info.getByRole('tab', { name: 'Медиа' }).click()
  await expect(info.locator('.grid .tile')).toHaveCount(3)
  await info.locator('.grid .tile').first().click()
  await dialog(b, 'Просмотр').getByRole('button', { name: 'Показать в чате' }).click()
  await expect(album).toBeInViewport()
})

test('a document downloads intact and safely', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  await privateChat(request, alice, bob)
  const a = await open(alice)
  const b = await open(bob)
  await openChat(a, bob.displayName)
  await openChat(b, alice.displayName)

  const content = randomBytes(300_000)
  await fileInput(a).setInputFiles({ name: 'отчёт за квартал.bin', mimeType: 'application/octet-stream', buffer: content })
  await expect(sendButton(a)).toBeEnabled({ timeout: 30_000 })
  await sendButton(a).click()

  const link = b.locator('.window .file a.name', { hasText: 'отчёт за квартал.bin' })
  await expect(link).toBeVisible()
  const href = await link.getAttribute('href')
  const response = await b.request.get(href!)
  expect(response.status()).toBe(200)
  expect(Buffer.compare(await response.body(), content)).toBe(0)
  expect(response.headers()['content-disposition']).toMatch(/attachment/)

  const info = await openInfo(b)
  await info.getByRole('tab', { name: 'Файлы' }).click()
  await expect(info.locator('.row')).toContainText('отчёт за квартал.bin')
})

test('an uploaded web page cannot run on the app origin', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  await privateChat(request, alice, bob)
  const a = await open(alice)
  const b = await open(bob)
  await openChat(a, bob.displayName)
  await openChat(b, alice.displayName)

  const html = '<html><body><script>document.title="pwned";window.opener&&(window.opener.__pwned=1)</script>hi</body></html>'
  await fileInput(a).setInputFiles({ name: 'invoice.html', mimeType: 'text/html', buffer: Buffer.from(html) })
  await expect(sendButton(a)).toBeEnabled({ timeout: 30_000 })
  await sendButton(a).click()

  const link = b.locator('.window .file a.name', { hasText: 'invoice.html' })
  await expect(link).toBeVisible()
  const href = (await link.getAttribute('href'))!
  const response = await b.request.get(href)
  const headers = response.headers()
  expect(headers['content-disposition']).toMatch(/attachment/)
  expect(headers['x-content-type-options']).toBe('nosniff')
  expect(headers['content-security-policy'] ?? '').toMatch(/sandbox/)

  // Even opened directly, it must not run as the app.
  const tab = await b.context().newPage()
  const download = tab.waitForEvent('download', { timeout: 5_000 }).catch(() => null)
  await tab.goto(href).catch(() => {})
  await download
  expect(await tab.title()).not.toBe('pwned')
  expect(await b.evaluate(() => (window as any).__pwned)).toBeUndefined()
})

test('wrong attachments are refused with a reason', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  await privateChat(request, alice, bob)
  const a = await open(alice)
  await openChat(a, bob.displayName)

  await fileInput(a).setInputFiles({ name: 'пусто.txt', mimeType: 'text/plain', buffer: Buffer.alloc(0) })
  await expect(notice(a, 'Пустой файл')).toBeVisible()

  await fileInput(a).setInputFiles([
    { name: 'кадр.png', mimeType: 'image/png', buffer: png() },
    { name: 'смета.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4 test') },
  ])
  await expect(notice(a, 'Фото и видео отправляются отдельно')).toBeVisible()
  await expect(a.locator('.tray .item')).toHaveCount(1)

  // Removing the only file: nothing to send.
  await a.locator('.tray .item').getByRole('button').click()
  await expect(a.locator('.tray')).toHaveCount(0)
  await expect(sendButton(a)).toBeDisabled()
})

test('a pasted screenshot is attached', async ({ open, user, request }) => {
  const alice = await user('alice')
  const bob = await user('bob')
  await privateChat(request, alice, bob)
  const a = await open(alice)
  const b = await open(bob)
  await openChat(a, bob.displayName)
  await openChat(b, alice.displayName)

  const bytes = [...png(200, 120, 3)]
  await composer(a).evaluate((el, data) => {
    const transfer = new DataTransfer()
    transfer.items.add(new File([new Uint8Array(data)], 'image.png', { type: 'image/png' }))
    el.dispatchEvent(new ClipboardEvent('paste', { clipboardData: transfer, bubbles: true, cancelable: true }))
  }, bytes)
  await expect(a.locator('.tray .item img.thumb')).toBeVisible()
  await expect(sendButton(a)).toBeEnabled({ timeout: 30_000 })
  await composer(a).press('Enter')
  await expect(b.locator('.window .item .tile img')).toHaveCount(1)
})

test('a large file goes through the small server', async ({ open, user, request }, testInfo) => {
  test.setTimeout(180_000)
  const alice = await user('alice')
  const bob = await user('bob')
  await privateChat(request, alice, bob)
  const a = await open(alice)
  const b = await open(bob)
  await openChat(a, bob.displayName)
  await openChat(b, alice.displayName)

  const content = randomBytes(60 * 1024 * 1024)
  const path = testInfo.outputPath('архив.zip')
  writeFileSync(path, content)
  await fileInput(a).setInputFiles(path)
  await expect(a.locator('.tray .item .bar')).toBeVisible()
  await expect(sendButton(a)).toBeEnabled({ timeout: 150_000 })
  await sendButton(a).click()

  const link = b.locator('.window .file a.name', { hasText: 'архив.zip' })
  await expect(link).toBeVisible()
  await expect(b.locator('.window .file .size')).toContainText('60 МБ')
  const response = await b.request.get((await link.getAttribute('href'))!, { timeout: 120_000 })
  expect(response.status()).toBe(200)
  expect(Buffer.compare(await response.body(), content)).toBe(0)
})
