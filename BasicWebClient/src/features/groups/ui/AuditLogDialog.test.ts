import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, expect, it, vi } from 'vitest'

import * as chatApi from '@/entities/chat/api'
import type { ChatDetail } from '@/entities/chat/types'
import * as usersApi from '@/entities/user/api'
import AuditLogDialog from './AuditLogDialog.vue'

vi.mock('@/entities/chat/api', () => ({ getChatDetail: vi.fn(), getAudit: vi.fn() }))
vi.mock('@/entities/user/api', () => ({ getUser: vi.fn() }))

const detail = {
  chatId: 'g1', type: 'group', title: 'Team', avatarId: null, createdBy: 'anna', myRole: 'owner',
  myPermissions: null, memberPermissions: null,
  participants: [{ userId: 'anna', username: 'anna', displayName: 'Анна', avatarId: null, role: 'owner' }],
} as ChatDetail

beforeEach(() => {
  setActivePinia(createPinia())
  vi.mocked(chatApi.getChatDetail).mockResolvedValue(detail)
  vi.mocked(chatApi.getAudit).mockResolvedValue({
    hasMore: false,
    nextCursor: null,
    items: [
      { id: 2, action: 'member_removed', actorId: 'anna', targetUserId: 'vera', data: null, createdAt: '2026-10-01T10:00:00Z' },
      { id: 1, action: 'member_removed', actorId: 'anna', targetUserId: 'gone', data: null, createdAt: '2026-10-01T09:00:00Z' },
    ],
  })
  vi.mocked(usersApi.getUser).mockImplementation(async (id) =>
    id === 'vera'
      ? { userId: 'vera', username: 'vera', displayName: 'Вера', avatarId: null }
      : Promise.reject(new Error('404')),
  )
})

it('people no longer in the group are named by their profile, once each', async () => {
  const wrapper = mount(AuditLogDialog, { props: { chatId: 'g1' } })
  await flushPromises()

  const lines = wrapper.findAll('.entry .text').map((e) => e.text())
  expect(lines).toEqual(['Анна: исключение — Вера', 'Анна: исключение — участник вне группы'])
  expect(usersApi.getUser).toHaveBeenCalledTimes(2)
  expect(usersApi.getUser).not.toHaveBeenCalledWith('anna')
  wrapper.unmount()
})
