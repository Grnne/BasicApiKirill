import { expect, it } from 'vitest'

import { ME, message } from '@/testing/fixtures'
import { hideSpoilers, messagePreview } from './preview'

it('private chat: just the text; own messages are marked', () => {
  expect(messagePreview(message({ text: 'hi' }), ME, false)).toBe('hi')
  expect(messagePreview(message({ text: 'hi', senderId: ME }), ME, false)).toBe('Вы: hi')
})

it('group: the sender name in front', () => {
  expect(messagePreview(message({ text: 'hi', senderName: 'Bob' }), ME, true)).toBe('Bob: hi')
})

it('a system message is its text, a file without caption says so', () => {
  expect(messagePreview(message({ type: 'system', text: 'Создана группа' }), ME, true)).toBe('Создана группа')
  const file = { id: 'a', kind: 'file', fileName: 'x.pdf' } as never
  expect(messagePreview(message({ type: 'media', text: '', attachments: [file] }), ME, false)).toBe('📎 Файл')
})

it('a spoiler stays covered: the row has no formatting to hide it', () => {
  // The bug: the chat list showed the hidden part in plain text, no click needed.
  const spoiler = { type: 'spoiler', offset: 14, length: 10, url: null, userId: null, language: null }
  expect(messagePreview(message({ text: 'the killer is the butler', entities: [spoiler] }), ME, false)).toBe('the killer is ▒▒▒▒')
  expect(hideSpoilers('a b c', [{ ...spoiler, offset: 0, length: 3 }, { ...spoiler, offset: 2, length: 3 }])).toBe('▒▒▒▒')
})
