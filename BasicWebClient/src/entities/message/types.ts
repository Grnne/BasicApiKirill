/* Типы сообщений. По BasicApi/Models/Dto/Message. */

export interface Message {
  id: string
  chatId: string
  senderId: string
  senderName: string
  text: string
  /** ISO-строка от сервера. Разбирать только через parseApiDate. */
  createdAt: string
  isRead: boolean
}

/**
 * Постраничный ответ с курсором. Страницы идут «назад по времени»:
 * nextCursor ведёт к более старым сообщениям.
 */
export interface CursorPage<T> {
  items: T[]
  nextCursor: string | null
  hasMore: boolean
}

export interface SearchMessagesResponse extends CursorPage<Message> {
  query: string
  totalCount: number
}
