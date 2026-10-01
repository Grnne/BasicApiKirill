import { privateChat, send } from './support/api'
import { expect, test } from './support/fixtures'
import { openSettings } from './support/ui'

// Needs a push service the browser can reach (the internet) and push keys on the server.
test('push: subscribe, get a notification while away, unsubscribe', async ({ open, user, request, baseURL }) => {
  test.setTimeout(90_000)
  const alice = await user('alice')
  const bob = await user('bob')
  const chatId = await privateChat(request, alice, bob)
  const origin = new URL(baseURL!).origin

  const b = await open(bob, { permissions: ['notifications'] })
  await openSettings(b)
  const section = b.locator('section.section', { has: b.getByRole('heading', { name: 'Уведомления' }) })
  const enable = section.getByRole('button', { name: 'Включить уведомления' })
  await expect(section.locator('.hint')).toBeVisible()
  test.skip(!(await enable.isVisible()), `push is not offered here: ${await section.locator('.hint').innerText()}`)

  await enable.click()
  const error = section.locator('.error')
  await expect(section.getByRole('button', { name: 'Выключить' }).or(error)).toBeVisible({ timeout: 30_000 })
  // Only the browser's own refusal (no push service under automation) is a reason to skip; an
  // error of the page or of our API is a failure.
  if (await error.isVisible()) {
    const text = await error.innerText()
    test.skip(text.startsWith('Браузер не смог подписаться'), `the browser could not subscribe: ${text}`)
    throw new Error(`push could not be turned on: ${text}`)
  }

  const devices = b.locator('section.section', { has: b.getByRole('heading', { name: 'Устройства' }) })
  await b.reload()
  await expect(devices.locator('.device', { hasText: 'это устройство' }).getByTitle('Получает уведомления')).toBeVisible()

  // Bob goes away: no live connection, so the message comes as a push.
  const worker = b.context().serviceWorkers()[0] ?? (await b.context().waitForEvent('serviceworker'))
  await b.goto('about:blank')
  await send(request, alice, chatId, 'ты где?')

  await expect
    .poll(
      () =>
        worker.evaluate(async () =>
          (await (self as any).registration.getNotifications()).map((n: Notification) => `${n.title}: ${n.body}`),
        ),
      { timeout: 45_000 },
    )
    .toContain(`${alice.displayName}: ты где?`)

  await b.goto(`${origin}/client/settings`)
  await section.getByRole('button', { name: 'Выключить' }).click()
  await expect(enable).toBeVisible()
})
