import type { Message } from '../types'

/**
 * A system message in words, with the names people have now. The server's text keeps the names of
 * the moment; it stays the answer for unknown actions and for people the client cannot name (they
 * have left the group, so the client no longer has them).
 */
export function systemCaption(message: Message, nameOf: (userId: string) => string | null): string {
  const action = message.action
  if (!action) return message.text

  const names = action.userIds.map(nameOf)
  if (names.some((n) => n === null)) return message.text
  const list = names.join(', ')

  switch (action.type) {
    case 'group_created':
      return `Создана группа «${action.title ?? ''}»`
    case 'title_changed':
      return `Название группы изменено на «${action.title ?? ''}»`
    case 'members_added':
      return (names.length === 1 ? 'Добавлен участник: ' : 'Добавлены участники: ') + list
    case 'member_removed':
      return `Участник ${list} исключён из группы`
    case 'member_left':
      return `Участник ${list} покинул группу`
    case 'photo_changed':
      return 'Фото группы изменено'
    case 'photo_removed':
      return 'Фото группы удалено'
    default:
      return message.text
  }
}
