import { defineConfig } from '@playwright/test'

/**
 * UI end-to-end tests against a deployed stack (Caddy → API → Postgres → storage):
 * scripts/e2e-ui.ps1, or `npx playwright test` with E2E_BASE_URL. They use an installed
 * browser (E2E_BROWSER: msedge by default, or chrome) — nothing is downloaded.
 */
const baseURL = (process.env.E2E_BASE_URL ?? 'https://localhost:8443').replace(/\/$/, '')
const local = /^https:\/\/(localhost|127\.0\.0\.1)(:|$)/.test(baseURL)

export default defineConfig({
  testDir: './e2e',
  outputDir: './e2e-results',
  timeout: 60_000,
  expect: { timeout: 10_000 },
  fullyParallel: true,
  workers: Number(process.env.E2E_WORKERS ?? 4),
  retries: 0,
  reporter: [['list'], ['html', { outputFolder: './e2e-report', open: 'never' }]],

  use: {
    baseURL,
    channel: process.env.E2E_BROWSER ?? 'msedge',
    // Caddy's local CA for localhost is not trusted by the browser.
    ignoreHTTPSErrors: local,
    // The service worker is refused over an untrusted certificate even with ignoreHTTPSErrors.
    launchOptions: local ? { args: ['--allow-insecure-localhost', '--ignore-certificate-errors'] } : {},
    locale: 'ru-RU',
    viewport: { width: 1280, height: 800 },
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    actionTimeout: 10_000,
  },

  projects: [
    { name: 'ui', testIgnore: /resilience\.spec\.ts/ },
    // Restarts the API: runs alone, after everything else.
    { name: 'resilience', testMatch: /resilience\.spec\.ts/, dependencies: ['ui'] },
  ],
})
