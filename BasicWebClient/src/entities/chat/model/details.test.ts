import { flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { GroupMemberDto, GroupPermissionsDto } from '@/shared/api/schema'
import type { ChatDetail } from '../types'
import * as chatApi from '../api'
import { applyDetailsEvent, type DetailsState } from './details'
import { useChatDetailsStore } from './details.store'

vi.mock('../api', () => ({ getChatDetail: vi.fn() }))

const ME = 'me'
const PERMS = {} as GroupPermissionsDto

const detail = (): ChatDetail => ({
  chatId: 'g1',
  type: 'group',
  title: 'Team',
  avatarId: null,
  createdBy: ME,
  myRole: 'member',
  myPermissions: PERMS,
  memberPermissions: PERMS,
  participants: [
    { userId: ME, username: 'me', displayName: 'Me', avatarId: null, role: 'member' },
    { userId: 'bob', username: 'bob', displayName: 'Bob', avatarId: null, role: 'owner' },
  ],
})

const member = (userId: string, extra: Partial<GroupMemberDto> = {}): GroupMemberDto => ({
  userId, username: userId, displayName: userId, avatarId: null, role: 'member', permissions: PERMS, ...extra,
})

describe('details reducer', () => {
  let state: DetailsState
  beforeEach(() => {
    state = { g1: detail() }
  })

  it('a member added twice (live, then from the journal) is there once', () => {
    const event = { chatId: 'g1', addedBy: 'bob', members: [member('carl')] }
    applyDetailsEvent(state, 'MemberAdded', event, ME)
    applyDetailsEvent(state, 'MemberAdded', event, ME)
    expect(state.g1!.participants.map((p) => p.userId)).toEqual([ME, 'bob', 'carl'])
  })

  it('a new photo or name of a user reaches every chat they are in', () => {
    state.g2 = { ...detail(), chatId: 'g2', participants: [{ ...detail().participants[1]! }] }
    applyDetailsEvent(state, 'UserUpdated', { userId: 'bob', username: 'bob', displayName: 'Robert', avatarId: 'av' }, ME)
    expect(state.g1!.participants[1]).toMatchObject({ displayName: 'Robert', avatarId: 'av', role: 'owner' })
    expect(state.g2!.participants[0]!.avatarId).toBe('av')
  })

  it('a removed member leaves; when it is me, the chat is forgotten', () => {
    applyDetailsEvent(state, 'MemberRemoved', { chatId: 'g1', userId: 'bob', removedBy: null }, ME)
    expect(state.g1!.participants.map((p) => p.userId)).toEqual([ME])

    applyDetailsEvent(state, 'MemberRemoved', { chatId: 'g1', userId: ME, removedBy: 'bob' }, ME)
    expect(state.g1).toBeUndefined()
  })

  it('my new role changes what I may do', () => {
    applyDetailsEvent(state, 'MemberUpdated', { chatId: 'g1', member: member(ME, { role: 'admin' }) }, ME)
    expect(state.g1!.myRole).toBe('admin')
    expect(state.g1!.participants[0]!.role).toBe('admin')
  })

  it('events of chats not loaded are ignored', () => {
    applyDetailsEvent(state, 'MemberAdded', { chatId: 'other', addedBy: 'bob', members: [member('carl')] }, ME)
    expect(Object.keys(state)).toEqual(['g1'])
  })
})

describe('details store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.mocked(chatApi.getChatDetail).mockReset()
  })

  it('asks of one chat go as one request; the members then come reactively', async () => {
    vi.mocked(chatApi.getChatDetail).mockResolvedValue(detail())
    const store = useChatDetailsStore()

    expect(store.member('g1', 'bob')).toBeNull()
    expect(store.member('g1', ME)).toBeNull()
    await flushPromises()

    expect(chatApi.getChatDetail).toHaveBeenCalledTimes(1)
    expect(store.member('g1', 'bob')!.displayName).toBe('Bob')
  })

  it('an event during the load may be older than the answer: the chat is loaded once more', async () => {
    let answer!: (d: ChatDetail) => void
    vi.mocked(chatApi.getChatDetail)
      .mockImplementationOnce(() => new Promise((resolve) => (answer = resolve)))
      .mockResolvedValueOnce({ ...detail(), participants: [...detail().participants, { ...detail().participants[0]!, userId: 'carl' }] })
    const store = useChatDetailsStore()

    store.get('g1')
    store.apply('MemberAdded', { chatId: 'g1', addedBy: 'bob', members: [member('carl')] }, ME)
    answer(detail())
    await flushPromises()

    expect(chatApi.getChatDetail).toHaveBeenCalledTimes(2)
    expect(store.get('g1')!.participants.map((p) => p.userId)).toContain('carl')
  })

  it('a failed load is not retried by every render', async () => {
    vi.mocked(chatApi.getChatDetail).mockRejectedValue(new Error('403'))
    const store = useChatDetailsStore()

    store.get('g1')
    await flushPromises()
    store.get('g1')
    await flushPromises()

    expect(chatApi.getChatDetail).toHaveBeenCalledTimes(1)
  })

  it('an answer requested before a snapshot is dropped', async () => {
    let answer!: (d: ChatDetail) => void
    vi.mocked(chatApi.getChatDetail).mockImplementationOnce(() => new Promise((resolve) => (answer = resolve)))
    const store = useChatDetailsStore()

    store.load('g1')
    store.invalidate()
    answer(detail())
    await flushPromises()

    vi.mocked(chatApi.getChatDetail).mockResolvedValue(detail())
    expect(store.get('g1')).toBeNull()
  })
})
