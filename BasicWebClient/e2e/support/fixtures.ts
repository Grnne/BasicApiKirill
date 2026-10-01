import { test as base, expect, type BrowserContext, type BrowserContextOptions, type Page } from '@playwright/test'

import { registerUser, type TestUser } from './api'

export { expect }

interface Fixtures {
  /** Registers a user through the API (setup, not the subject of the test). */
  user: (name: string, displayName?: string) => Promise<TestUser>
  /** A separate browser — its own storage, as another device — signed in through the login form. */
  open: (user: TestUser, options?: BrowserContextOptions) => Promise<Page>
  /** A browser that is not signed in. */
  anonymous: (options?: BrowserContextOptions) => Promise<Page>
}

/**
 * Every page fails its test on an uncaught error or a CSP violation: those break the app for a
 * user without failing any assertion.
 */
function watch(page: Page, problems: string[]): void {
  page.on('pageerror', (error) => problems.push(`pageerror: ${error.message}`))
  page.on('console', (message) => {
    const text = message.text()
    if (/Content Security Policy|Refused to (load|execute|connect|frame|apply)/i.test(text)) {
      problems.push(`csp: ${text}`)
    }
  })
}

export const test = base.extend<Fixtures>({
  user: async ({ request }, use) => {
    await use((name, displayName) => registerUser(request, name, displayName))
  },

  open: async ({ browser }, use, testInfo) => {
    const contexts: BrowserContext[] = []
    const problems: string[] = []
    await use(async (user, options) => {
      const context = await browser.newContext(options)
      contexts.push(context)
      const page = await context.newPage()
      watch(page, problems)
      await signIn(page, user)
      return page
    })
    for (const context of contexts) await context.close()
    if (testInfo.status === testInfo.expectedStatus) expect(problems, problems.join('\n')).toEqual([])
  },

  anonymous: async ({ browser }, use, testInfo) => {
    const contexts: BrowserContext[] = []
    const problems: string[] = []
    await use(async (options) => {
      const context = await browser.newContext(options)
      contexts.push(context)
      const page = await context.newPage()
      watch(page, problems)
      return page
    })
    for (const context of contexts) await context.close()
    if (testInfo.status === testInfo.expectedStatus) expect(problems, problems.join('\n')).toEqual([])
  },
})

export async function signIn(page: Page, user: TestUser): Promise<void> {
  await page.goto('/client/login')
  await page.getByLabel('Логин или email').fill(user.username)
  await page.getByLabel('Пароль').fill(user.password)
  await page.getByRole('button', { name: 'Войти' }).click()
  await expect(page).toHaveURL(/\/client\/chat/)
  await expect(page.locator('.status[data-state="connected"]')).toBeVisible()
}
