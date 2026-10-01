import { expect, it } from 'vitest'

import { ApiError } from '@/shared/api/problem'
import { describeAddError } from './refusal'

const picked = [
  { userId: 'u1', displayName: 'Анна' },
  { userId: 'u2', displayName: 'Борис' },
  { userId: 'u3', displayName: 'Вера' },
]

it('a privacy refusal names who refused, of those picked', () => {
  const refused = new ApiError(403, { errorCode: 'PRIVACY_RESTRICTED', userIds: ['u2', 'u3'] })
  expect(describeAddError(refused, picked)).toBe('Не разрешают добавлять себя в группы: Борис, Вера. Уберите их и попробуйте снова')
})

it('any other error, or one without names, says what it says', () => {
  expect(describeAddError(new ApiError(403, { errorCode: 'PRIVACY_RESTRICTED' }), picked))
    .toBe('Пользователь ограничил, кто может ему писать или добавлять его')
})
