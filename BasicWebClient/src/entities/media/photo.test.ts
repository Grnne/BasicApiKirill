import { expect, it } from 'vitest'

import { DEFAULT_CONFIG } from '@/entities/config/config.store'
import { rejectAvatar } from './photo'

const file = (type: string, size: number) => ({ type, size, name: 'x' }) as File

it('an avatar: a photo of a known kind and size; the reason is the one that holds', () => {
  const max = DEFAULT_CONFIG.media.maxPhotoSize
  expect(rejectAvatar(file('image/jpeg', 1_000), DEFAULT_CONFIG)).toBeNull()
  expect(rejectAvatar(file('application/pdf', 1_000), DEFAULT_CONFIG)).toBe('Нужна фотография: JPEG, PNG, GIF или WebP')
  // The bug: a JPEG over the limit was called the wrong kind of file.
  expect(rejectAvatar(file('image/jpeg', max + 1), DEFAULT_CONFIG)).toMatch(/^Фото больше /)
})
