import type { ChatListItemDto, MessageDto } from '@/shared/api/schema'

export type OwnStatus = 'sent' | 'delivered' | 'read'

/**
 * The status of one's own message from the chat's pointers, not from message.status: events carry
 * no status, and the pointers move with MessagesDelivered / MessagesRead for all messages at once.
 * Null for others' messages and in "Saved messages" (nobody else to read them).
 */
export function ownStatus(message: MessageDto, chat: ChatListItemDto | null, meId: string | null): OwnStatus | null {
  if (!chat || message.senderId !== meId || chat.type === 'saved' || message.type === 'system') return null
  if (message.seq <= chat.outboxReadSeq) return 'read'
  if (message.seq <= chat.outboxDeliveredSeq) return 'delivered'
  return 'sent'
}

export const STATUS_MARKS: Record<OwnStatus, { mark: string; title: string }> = {
  sent: { mark: '✓', title: 'Отправлено' },
  delivered: { mark: '✓✓', title: 'Доставлено' },
  read: { mark: '✓✓', title: 'Прочитано' },
}
