// Loaded chat details (members with their names and photos) as a pure state machine. Events replay
// on catch-up, so each one either replaces what it touches or removes it: applying twice is harmless.

import type { GroupMemberDto } from '@/shared/api/schema'
import type { JournaledEventName, JournaledEvents } from '@/shared/api/hub.types'
import type { ChatDetail, ChatParticipant } from '../types'

export type DetailsState = Record<string, ChatDetail>

/** The events that change details. */
export const DETAIL_EVENTS: ReadonlySet<JournaledEventName> = new Set<JournaledEventName>([
  'MemberAdded',
  'MemberUpdated',
  'MemberRemoved',
  'UserUpdated',
  'ChatUpdated',
  'ChatDeleted',
])

const participant = (m: GroupMemberDto): ChatParticipant => ({
  userId: m.userId,
  username: m.username,
  displayName: m.displayName,
  avatarId: m.avatarId,
  role: m.role,
})

function upsert(detail: ChatDetail, member: ChatParticipant): void {
  const index = detail.participants.findIndex((p) => p.userId === member.userId)
  if (index === -1) detail.participants.push(member)
  else detail.participants[index] = member
}

/** Only chats already loaded change; the rest are fetched in full when someone needs them. */
export function applyDetailsEvent<K extends JournaledEventName>(
  state: DetailsState,
  type: K,
  payload: JournaledEvents[K],
  meId: string,
): void {
  switch (type) {
    case 'MemberAdded': {
      const added = payload as JournaledEvents['MemberAdded']
      const detail = state[added.chatId]
      if (detail) for (const m of added.members) upsert(detail, participant(m))
      return
    }
    case 'MemberUpdated': {
      const updated = payload as JournaledEvents['MemberUpdated']
      const detail = state[updated.chatId]
      if (!detail) return
      upsert(detail, participant(updated.member))
      if (updated.member.userId === meId) {
        detail.myRole = updated.member.role
        detail.myPermissions = updated.member.permissions
      }
      return
    }
    case 'MemberRemoved': {
      const removed = payload as JournaledEvents['MemberRemoved']
      if (removed.userId === meId) {
        delete state[removed.chatId]
        return
      }
      const detail = state[removed.chatId]
      if (detail) detail.participants = detail.participants.filter((p) => p.userId !== removed.userId)
      return
    }
    case 'UserUpdated': {
      const user = payload as JournaledEvents['UserUpdated']
      for (const detail of Object.values(state)) {
        const known = detail.participants.find((p) => p.userId === user.userId)
        if (known) upsert(detail, { ...known, displayName: user.displayName, username: user.username, avatarId: user.avatarId })
      }
      return
    }
    case 'ChatUpdated': {
      const chat = payload as JournaledEvents['ChatUpdated']
      const detail = state[chat.chatId]
      if (detail) {
        detail.title = chat.title
        detail.avatarId = chat.avatarId
        detail.memberPermissions = chat.memberPermissions
      }
      return
    }
    case 'ChatDeleted':
      delete state[(payload as JournaledEvents['ChatDeleted']).chatId]
      return
    default:
      return
  }
}
