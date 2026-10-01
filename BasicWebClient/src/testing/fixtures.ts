// Test data builders: a complete DTO with overrides.

import type { ChatListItemDto, MessageDto } from '@/shared/api/schema'

export const ME = 'me-0000'
export const BOB = 'bob-0000'

let counter = 0
const nextId = (prefix: string) => `${prefix}-${++counter}`

export function message(overrides: Partial<MessageDto> = {}): MessageDto {
  const seq = overrides.seq ?? 1
  return {
    id: nextId('msg'),
    chatId: 'chat-1',
    senderId: BOB,
    senderName: 'Bob',
    text: `message ${seq}`,
    createdAt: new Date(Date.UTC(2026, 9, 1, 12, 0, seq)).toISOString(),
    isRead: false,
    status: null,
    seq,
    clientMessageId: null,
    type: 'text',
    editedAt: null,
    entities: [],
    reactions: [],
    myReaction: null,
    replyTo: null,
    forwardFrom: null,
    attachments: [],
    action: null,
    ...overrides,
  }
}

export function chat(overrides: Partial<ChatListItemDto> = {}): ChatListItemDto {
  return {
    chatId: 'chat-1',
    type: 'private',
    title: null,
    companionId: BOB,
    companionName: 'Bob',
    companionUsername: 'bob',
    avatarId: null,
    lastMessage: null,
    unreadCount: 0,
    unreadMentionCount: 0,
    lastReadSeq: 0,
    markedUnread: false,
    outboxReadSeq: 0,
    outboxDeliveredSeq: 0,
    lastActivityAt: new Date(Date.UTC(2026, 9, 1, 12, 0, 0)).toISOString(),
    pinnedPosition: null,
    archived: false,
    isMuted: false,
    mutedUntil: null,
    draft: null,
    ...overrides,
  }
}
