// Who may do what with a group's members: the server's rules, checked before a button is shown.
// The server checks again; these only keep the card from offering what would be refused.

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
