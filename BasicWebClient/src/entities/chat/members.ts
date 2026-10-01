// Who may do what with a group's members: the server's rules, checked before a button is shown.
// The server checks again; these only keep the card from offering what would be refused.

import type { GroupPermissionsDto } from '@/shared/api/schema'
import type { ChatDetail, ChatParticipant } from './types'

export const ROLE_LABELS: Readonly<Record<string, string>> = { owner: 'владелец', admin: 'админ' }

const RANK: Readonly<Record<string, number>> = { owner: 0, admin: 1 }

/** The owner, then admins, then members; by name inside a role. */
export function sortedMembers(participants: readonly ChatParticipant[]): ChatParticipant[] {
  return [...participants].sort(
    (a, b) => (RANK[a.role] ?? 2) - (RANK[b.role] ?? 2) || a.displayName.localeCompare(b.displayName, 'ru'),
  )
}

export function canAddMembers(detail: ChatDetail): boolean {
  return detail.type === 'group' && !!detail.myPermissions?.addMembers
}

/** The owner removes anyone; an admin with removeMembers — members only. Oneself — that is leaving. */
export function canRemoveMember(detail: ChatDetail, meId: string, target: ChatParticipant): boolean {
  if (detail.type !== 'group' || target.userId === meId) return false
  if (detail.myRole === 'owner') return true
  return detail.myRole === 'admin' && !!detail.myPermissions?.removeMembers && target.role === 'member'
}

export type Permission = keyof GroupPermissionsDto

/** In the order a settings list shows them. */
export const PERMISSION_LABELS: Readonly<Record<Permission, string>> = {
  sendMessages: 'Писать сообщения',
  sendMedia: 'Отправлять файлы',
  addMembers: 'Добавлять участников',
  changeInfo: 'Менять название и фото',
  deleteMessages: 'Удалять чужие сообщения',
  removeMembers: 'Исключать участников',
  addAdmins: 'Назначать админов',
}

/** What the group's defaults and a member's overrides may hold; the rest is for admins. */
export const MEMBER_PERMISSIONS: readonly Permission[] = ['sendMessages', 'sendMedia', 'addMembers', 'changeInfo']

const isOwner = (detail: ChatDetail) => detail.myRole === 'owner'
const isAdmin = (detail: ChatDetail) => detail.myRole === 'admin'

export const canEditInfo = (detail: ChatDetail) => detail.type === 'group' && !!detail.myPermissions?.changeInfo

/** The group's defaults: the owner, or an admin with removeMembers. */
export const canEditDefaults = (detail: ChatDetail) =>
  detail.type === 'group' && (isOwner(detail) || (isAdmin(detail) && !!detail.myPermissions?.removeMembers))

export const canDeleteGroup = (detail: ChatDetail) => detail.type === 'group' && isOwner(detail)

export const canSeeAudit = (detail: ChatDetail) => detail.type === 'group' && (isOwner(detail) || isAdmin(detail))

export interface MemberActions {
  makeAdmin: boolean
  makeMember: boolean
  handOver: boolean
  /** The permissions that may be set for this member; empty — none. */
  permissions: readonly Permission[]
}

/** What the caller may change about another member: role and own permissions. */
export function memberActions(detail: ChatDetail, meId: string, target: ChatParticipant): MemberActions {
  const none: MemberActions = { makeAdmin: false, makeMember: false, handOver: false, permissions: [] }
  if (detail.type !== 'group' || target.userId === meId || target.role === 'owner') return none

  const owner = isOwner(detail)
  const may = detail.myPermissions
  const targetIsMember = target.role === 'member'

  // A member's permissions: the owner or an admin with removeMembers; an admin's — the owner, all of them.
  let permissions: readonly Permission[] = []
  if (targetIsMember && (owner || (isAdmin(detail) && !!may?.removeMembers))) permissions = MEMBER_PERMISSIONS
  else if (!targetIsMember && owner) permissions = Object.keys(PERMISSION_LABELS) as Permission[]

  return {
    makeAdmin: targetIsMember && (owner || (isAdmin(detail) && !!may?.addAdmins)),
    makeMember: !targetIsMember && owner,
    handOver: owner,
    permissions,
  }
}
