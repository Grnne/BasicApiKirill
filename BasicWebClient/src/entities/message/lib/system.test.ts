import { expect, it } from 'vitest'

import { message } from '@/testing/fixtures'
import { systemCaption } from './system'

const names: Record<string, string> = { bob: 'Robert', carl: 'Carl' }
const nameOf = (id: string) => names[id] ?? null

const system = (type: string, userIds: string[], text = 'server text', title: string | null = null) =>
  message({ type: 'system', text, action: { type, userIds, title } })

it('names come as they are now, not as they were', () => {
  expect(systemCaption(system('members_added', ['bob'], 'Добавлен участник: Bob'), nameOf)).toBe('Добавлен участник: Robert')
  expect(systemCaption(system('members_added', ['bob', 'carl']), nameOf)).toBe('Добавлены участники: Robert, Carl')
  expect(systemCaption(system('member_left', ['carl']), nameOf)).toBe('Участник Carl покинул группу')
  expect(systemCaption(system('title_changed', [], 'x', 'New'), nameOf)).toBe('Название группы изменено на «New»')
})

it('someone the client cannot name, an unknown action or no action — the server text', () => {
  expect(systemCaption(system('member_removed', ['gone'], 'Участник Dan исключён из группы'), nameOf)).toBe(
    'Участник Dan исключён из группы',
  )
  expect(systemCaption(system('pinned_message', [], 'Закреплено'), nameOf)).toBe('Закреплено')
  expect(systemCaption(message({ type: 'system', text: 'plain' }), nameOf)).toBe('plain')
})
