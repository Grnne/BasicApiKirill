import { execFileSync } from 'node:child_process'

import { privateChat, send } from './support/api'
import { expect, test } from './support/fixtures'
import { chatRow, message, openChat, sendText } from './support/ui'

// Restarts containers of the stack: only where it is allowed (scripts/e2e-ui.ps1 -Restart).
const allowed = process.env.E2E_RESTART === '1'
const container = (name: string) => process.env[`E2E_${name.toUpperCase()}_CONTAINER`] ?? `basicchat_${name}`

function restart(name: string): void {
  execFileSync('docker', ['restart', container(name)], { stdio: 'ignore' })
}

test.describe.configure({ mode: 'serial' })
test.skip(!allowed, 'restarts the stack: set E2E_RESTART=1')

test('an API restart: clients reconnect, catch up, and what was typed goes out', async ({ open, user, request }) => {
  test.setTimeout(120_000)
  const alice = await user('alice')
  const bob = await user('bob')
  const carol = await user('carol')
  const chatId = await privateChat(request, alice, bob)
  const withCarol = await privateChat(request, carol, alice)
  const a = await open(alice)
  const b = await open(bob)
  await openChat(a, bob.displayName)
  await openChat(b, alice.displayName)

  restart('api')
  await expect(a.locator('.status[data-state="connected"]')).toBeHidden({ timeout: 10_000 }).catch(() => {})
  await sendText(a, 'пока сервер лежал')

  await expect(a.locator('.status[data-state="connected"]')).toBeVisible({ timeout: 60_000 })
  await expect(b.locator('.status[data-state="connected"]')).toBeVisible({ timeout: 60_000 })
  await expect(message(b, 'пока сервер лежал')).toHaveCount(1, { timeout: 30_000 })

  // Live again, both ways, and in the list.
  await send(request, bob, chatId, 'после рестарта')
  await expect(message(a, 'после рестарта')).toBeVisible()
  await send(request, carol, withCarol, 'и в другом чате')
  await expect(chatRow(a, carol.displayName)).toContainText('и в другом чате')
})

test('a database restart: sending fails over and recovers without doubles', async ({ open, user, request }) => {
  test.setTimeout(150_000)
  const alice = await user('alice')
  const bob = await user('bob')
  await privateChat(request, alice, bob)
  const a = await open(alice)
  const b = await open(bob)
  await openChat(a, bob.displayName)
  await openChat(b, alice.displayName)

  restart('postgres')
  await sendText(a, 'во время рестарта базы')
  const failed = a.locator('.window .bubble.failed')
  await expect(a.locator('.window .bubble.sending')).toHaveCount(0, { timeout: 60_000 })
  if (await failed.count()) await failed.getByRole('button', { name: 'Повторить' }).click()

  await expect(message(b, 'во время рестарта базы')).toHaveCount(1, { timeout: 60_000 })
  await sendText(b, 'база жива')
  await expect(message(a, 'база жива')).toBeVisible({ timeout: 30_000 })
  await a.reload()
  await openChat(a, bob.displayName)
  await expect(message(a, 'во время рестарта базы')).toHaveCount(1)
})

test('many clients at once keep up after a restart', async ({ open, user, request }) => {
  test.setTimeout(180_000)
  const owner = await user('owner')
  const people = await Promise.all(Array.from({ length: 8 }, (_, i) => user(`member${i}`)))
  const chats = await Promise.all(people.map((p) => privateChat(request, owner, p)))
  const pages = []
  for (const person of people) pages.push(await open(person))

  restart('api')
  for (const page of pages) await expect(page.locator('.status[data-state="connected"]')).toBeVisible({ timeout: 60_000 })

  await Promise.all(chats.map((chatId, i) => send(request, owner, chatId, `всем привет ${i}`)))
  for (const [i, page] of pages.entries()) {
    await expect(chatRow(page, owner.displayName)).toContainText(`всем привет ${i}`)
  }
})
