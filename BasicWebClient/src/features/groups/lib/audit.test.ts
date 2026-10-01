import { expect, it } from 'vitest'

import type { AuditEntryDto } from '@/shared/api/schema'
import { auditText } from './audit'

const names: Record<string, string> = { anna: 'Анна', bob: 'Борис' }
const name = (id: string) => names[id] ?? null

const entry = (action: string, data: unknown = null, targetUserId: string | null = null, actorId: string | null = 'anna'): AuditEntryDto => ({
  id: 1, action, actorId, targetUserId, data, createdAt: '2026-10-01T10:00:00Z',
})

it('says what happened, with names the client knows', () => {
  expect(auditText(entry('title_changed', { from: 'A', to: 'B' }), name)).toBe('Анна: название «A» → «B»')
  expect(auditText(entry('role_changed', { role: 'admin' }, 'bob'), name)).toBe('Анна: роль «админ» — Борис')
  expect(auditText(entry('members_added', { userIds: ['bob', 'carl'] }), name)).toBe(
    'Анна: добавление — Борис, участник вне группы',
  )
  expect(auditText(entry('ownership_transferred', { reason: 'owner_left' }, 'bob'), name)).toBe(
    'Анна: выход из группы, новый владелец — Борис',
  )
})

it('permissions are listed as set; a deleted account and an unknown action still read', () => {
  expect(auditText(entry('permissions_changed', { permissions: { sendMedia: false, junk: 1 } }, 'bob'), name)).toBe(
    'Анна: права участника Борис — Отправлять файлы: нет',
  )
  expect(auditText(entry('member_permissions_changed', { memberPermissions: {} }), name)).toContain('как в группе')
  expect(auditText(entry('member_left', null, null, null), name)).toBe('удалённый аккаунт: выход из группы')
  expect(auditText(entry('something_new'), name)).toBe('Анна: something_new')
})
