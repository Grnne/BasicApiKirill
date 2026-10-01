// @vitest-environment node
import { readFileSync } from 'node:fs'
import { expect, it } from 'vitest'

import { generate, OUTPUT } from '../../../scripts/gen-api.mjs'

const lf = (text: string) => text.replace(/\r\n/g, '\n')

it('schema.d.ts matches the OpenAPI snapshot (npm run gen:api)', async () => {
  expect(lf(readFileSync(OUTPUT, 'utf8'))).toBe(lf(await generate()))
}, 30_000)
