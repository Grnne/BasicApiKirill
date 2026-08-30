/* Мелкие функции отображения чата. Держим их рядом с типом, а не в компонентах:
   заголовок чата нужен и в списке, и в шапке переписки. */

import type { ChatListItem } from './types'

/** Название приватного чата — имя собеседника, группового — его заголовок. */
export function chatTitle(chat: ChatListItem): string {
  if (chat.type === 'private') {
    return chat.companionName || chat.companionUsername || 'Без имени'
  }
  return chat.title || 'Без названия'
}

/** Одна буква для кружка-аватарки. */
export function chatInitial(chat: ChatListItem): string {
  return chatTitle(chat).trim().charAt(0).toUpperCase() || '?'
}
