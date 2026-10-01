import type { MessageDto, MessageEntityDto } from '@/shared/api/schema'

/** What a spoiler shows as in plain text — the same cover the server uses (MessageEntities). */
const SPOILER_COVER = '▒▒▒▒'

/** The text with spoilers covered, for places without formatting. Overlapping ones are covered once. */
export function hideSpoilers(text: string, entities: readonly MessageEntityDto[] | null | undefined): string {
  const spoilers = (entities ?? []).filter((e) => e.type === 'spoiler').sort((a, b) => a.offset - b.offset)
  if (spoilers.length === 0) return text
  let result = ''
  let at = 0
  let covering = false
  for (const spoiler of spoilers) {
    const start = Math.min(Math.max(spoiler.offset, 0), text.length)
    const end = Math.min(Math.max(spoiler.offset + spoiler.length, start), text.length)
    if (end <= start || end <= at) continue
    // One that overlaps or touches the covered part extends it.
    if (!covering || start > at) result += text.slice(at, start) + SPOILER_COVER
    at = end
    covering = true
  }
  return result + text.slice(at)
}

/** One line for the chat list: who and what, without markup. */
export function messagePreview(message: MessageDto, meId: string | null, isGroup: boolean): string {
  if (message.type === 'system') return message.text

  const body = hideSpoilers(message.text, message.entities) || (message.attachments.length > 0 ? '📎 Файл' : 'Сообщение')
  if (message.senderId === meId) return `Вы: ${body}`
  return isGroup ? `${message.senderName}: ${body}` : body
}
