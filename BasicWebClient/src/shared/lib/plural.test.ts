import { expect, it } from 'vitest'

import { plural } from './plural'

it('picks the Russian form by the number', () => {
  const forms = ['участник', 'участника', 'участников'] as const
  expect([1, 2, 5, 11, 12, 21, 22, 25, 111, 0].map((n) => plural(n, forms))).toEqual([
    'участник', 'участника', 'участников', 'участников', 'участников',
    'участник', 'участника', 'участников', 'участников', 'участников',
  ])
})
