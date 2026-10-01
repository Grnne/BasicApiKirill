import { PERMISSION_LABELS, type Permission } from '@/entities/chat/members'
import type { AuditEntryDto } from '@/shared/api/schema'

type Data = Record<string, unknown>

const ROLES: Readonly<Record<string, string>> = { owner: 'владелец', admin: 'админ', member: 'участник' }

const asData = (value: unknown): Data => (value && typeof value === 'object' ? (value as Data) : {})
const text = (value: unknown) => (typeof value === 'string' ? value : '')

/** "Писать сообщения: да, Отправлять файлы: нет"; a field left to the defaults is not listed. */
function permissionsText(value: unknown): string {
  const changes = Object.entries(asData(value))
    .filter(([key, on]) => key in PERMISSION_LABELS && typeof on === 'boolean')
    .map(([key, on]) => `${PERMISSION_LABELS[key as Permission]}: ${on ? 'да' : 'нет'}`)
  return changes.length > 0 ? changes.join(', ') : 'как в группе'
}

/**
 * One log entry in words, as "who: what — whom": a Russian name does not tell the verb's gender.
 * `name` gives the name of a user the client knows; the log outlives memberships, so an unknown one
 * is "участник вне группы", a deleted account is "удалённый аккаунт".
 */
export function auditText(entry: AuditEntryDto, name: (userId: string) => string | null): string {
  const who = (id: string | null) => (id === null ? 'удалённый аккаунт' : name(id) ?? 'участник вне группы')
  const actor = who(entry.actorId)
  const target = who(entry.targetUserId)
  const data = asData(entry.data)

  switch (entry.action) {
    case 'group_created':
      return `${actor}: создание группы «${text(data.title)}»`
    case 'title_changed':
      return `${actor}: название «${text(data.from)}» → «${text(data.to)}»`
    case 'photo_changed':
      return `${actor}: новое фото группы`
    case 'photo_removed':
      return `${actor}: фото группы удалено`
    case 'members_added': {
      const ids = Array.isArray(data.userIds) ? data.userIds.filter((id): id is string => typeof id === 'string') : []
      return `${actor}: добавление — ${ids.map(who).join(', ')}`
    }
    case 'member_removed':
      return `${actor}: исключение — ${target}`
    case 'member_left':
      return `${actor}: выход из группы`
    case 'role_changed':
      return `${actor}: роль «${ROLES[text(data.role)] ?? text(data.role)}» — ${target}`
    case 'ownership_transferred':
      return data.reason === 'owner_left'
        ? `${actor}: выход из группы, новый владелец — ${target}`
        : `${actor}: передача группы — ${target}`
    case 'permissions_changed':
      return `${actor}: права участника ${target} — ${permissionsText(data.permissions)}`
    case 'member_permissions_changed':
      return `${actor}: права участников по умолчанию — ${permissionsText(data.memberPermissions)}`
    case 'message_deleted':
      return `${actor}: удаление сообщения, автор — ${target}`
    default:
      return `${actor}: ${entry.action}`
  }
}
