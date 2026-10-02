import { describe, expect, it } from 'vitest'

import type { GroupPermissionsDto } from '@/shared/api/schema'
import { canAddMembers, canEditDefaults, canRemoveMember, mayGive, MEMBER_PERMISSIONS, memberActions, sortedMembers } from './members'
import type { ChatDetail, ChatParticipant } from './types'

const perms = (p: Partial<GroupPermissionsDto> = {}): GroupPermissionsDto => ({
  addAdmins: false, addMembers: false, changeInfo: false, deleteMessages: false, removeMembers: false,
  sendMedia: true, sendMessages: true, ...p,
})
const person = (userId: string, role = 'member', displayName = userId): ChatParticipant => ({
  userId, username: userId, displayName, avatarId: null, role,
})
const OWNER = person('owner', 'owner')
const ADMIN = person('admin', 'admin')
const MEMBER = person('member')

const detail = (myRole: string, my: Partial<GroupPermissionsDto> = {}): ChatDetail => ({
  chatId: 'g', type: 'group', title: 'G', avatarId: null, createdBy: 'owner', myRole,
  myPermissions: perms(my), memberPermissions: perms(), participants: [OWNER, ADMIN, MEMBER],
})

describe('group members', () => {
  it('the owner removes anyone but themselves', () => {
    const d = detail('owner')
    expect([ADMIN, MEMBER].every((p) => canRemoveMember(d, 'owner', p))).toBe(true)
    expect(canRemoveMember(d, 'owner', OWNER)).toBe(false)
  })

  it('an admin removes members only, and only with removeMembers', () => {
    expect(canRemoveMember(detail('admin', { removeMembers: true }), 'admin', MEMBER)).toBe(true)
    expect(canRemoveMember(detail('admin', { removeMembers: true }), 'admin', OWNER)).toBe(false)
    expect(canRemoveMember(detail('admin'), 'admin', MEMBER)).toBe(false)
  })

  it('a member removes nobody; adds when the group lets them', () => {
    expect(canRemoveMember(detail('member', { removeMembers: true }), 'member', ADMIN)).toBe(false)
    expect(canAddMembers(detail('member'))).toBe(false)
    expect(canAddMembers(detail('member', { addMembers: true }))).toBe(true)
  })

  it('the owner makes admins, members again, hands the group over and sets anything for an admin', () => {
    const owner = detail('owner')
    expect(memberActions(owner, 'owner', MEMBER)).toMatchObject({ makeAdmin: true, makeMember: false, handOver: true })
    expect(memberActions(owner, 'owner', MEMBER).permissions).toEqual(MEMBER_PERMISSIONS)
    expect(memberActions(owner, 'owner', ADMIN)).toMatchObject({ makeAdmin: false, makeMember: true })
    expect(memberActions(owner, 'owner', ADMIN).permissions).toContain('addAdmins')
  })

  it('an admin: admins only with addAdmins, member permissions only with removeMembers, nothing about other admins', () => {
    expect(memberActions(detail('admin', { addAdmins: true }), 'admin', MEMBER)).toMatchObject({
      makeAdmin: true, handOver: false, permissions: [],
    })
    expect(memberActions(detail('admin', { removeMembers: true }), 'admin', MEMBER).permissions).toEqual(MEMBER_PERMISSIONS)
    const other = person('admin2', 'admin')
    expect(memberActions(detail('admin', { addAdmins: true, removeMembers: true }), 'admin', other)).toEqual({
      makeAdmin: false, makeMember: false, handOver: false, permissions: [],
    })
    expect(canEditDefaults(detail('admin'))).toBe(false)
    expect(canEditDefaults(detail('admin', { removeMembers: true }))).toBe(true)
  })

  it('an admin gives only the permissions they have; the owner gives any', () => {
    expect(mayGive(detail('admin', { removeMembers: true, changeInfo: false }), 'changeInfo')).toBe(false)
    expect(mayGive(detail('admin', { removeMembers: true, addMembers: true }), 'addMembers')).toBe(true)
    expect(mayGive(detail('owner'), 'changeInfo')).toBe(true)
  })

  it('nobody changes the owner', () => {
    expect(memberActions(detail('admin', { addAdmins: true, removeMembers: true }), 'admin', OWNER).permissions).toEqual([])
  })

  it('the owner first, then admins, then members by name', () => {
    const list = [person('b', 'member', 'Яна'), person('c', 'admin'), person('a', 'member', 'Анна'), OWNER]
    expect(sortedMembers(list).map((p) => p.userId)).toEqual(['owner', 'c', 'a', 'b'])
  })
})
