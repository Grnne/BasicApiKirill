import type { MessageDto } from '@/shared/api/schema'

/** One line for the chat list: who and what, without markup. */
export function messagePreview(message: MessageDto, meId: string | null, isGroup: boolean): string {
  if (message.type === 'system') return message.text

  const body = message.text || (message.attachments.length > 0 ? '📎 Файл' : 'Сообщение')
  if (message.senderId === meId) return `Вы: ${body}`
  return isGroup ? `${message.senderName}: ${body}` : body
}
