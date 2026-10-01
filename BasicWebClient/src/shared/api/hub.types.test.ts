// @vitest-environment node
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, it } from 'vitest'

import { JOURNALED_EVENT_NAMES } from './hub.types'

const PUBLISHER = fileURLToPath(
  new URL('../../../../BasicApi/Services/Events/OutboxChatEventPublisher.cs', import.meta.url),
)

it('knows every journaled event type of the server (UpdateTypes)', () => {
  const source = readFileSync(PUBLISHER, 'utf8')
  const body = source.slice(source.indexOf('class UpdateTypes'))
  const server = [...body.matchAll(/public const string (\w+) = "(\w+)";/g)].map((m) => m[2])

  expect(server.length).toBeGreaterThan(0)
  expect([...JOURNALED_EVENT_NAMES].sort()).toEqual(server.sort())
})
