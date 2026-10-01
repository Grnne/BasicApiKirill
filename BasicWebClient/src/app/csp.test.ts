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
