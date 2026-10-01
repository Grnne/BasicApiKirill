// @vitest-environment node
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, it } from 'vitest'

import { devOnlyCsp } from '../../vite.config'

const INDEX = fileURLToPath(new URL('../../index.html', import.meta.url))
const META = /<meta\s+http-equiv="Content-Security-Policy"/

it('the dev page has a CSP meta, the build drops it for the server header', () => {
  const html = readFileSync(INDEX, 'utf8')
  expect(html).toMatch(META)

  const transform = devOnlyCsp().transformIndexHtml as (html: string) => string
  const built = transform(html)
  expect(built).not.toMatch(META)
  expect(built).toContain('<div id="app"></div>')
})

it('the dev page lets scripts talk to its own origin only, WebSockets included', () => {
  // "ws: wss:" allowed sockets to any host, against the point of connect-src 'self'; the hub and
  // Vite's HMR are same-origin (the dev server proxies /hubs), which 'self' covers.
  const html = readFileSync(INDEX, 'utf8')
  const policy = /content="(default-src[^"]+)"/.exec(html)?.[1] ?? ''
  const connect = /connect-src ([^;]+)/.exec(policy)?.[1]?.trim()
  expect(connect).toBe("'self'")
})
