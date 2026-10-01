import type { MessageDto } from '@/shared/api/schema'

export interface MessageActions {
  reply: boolean
  forward: boolean
  copy: boolean
  edit: boolean
  deleteForMe: boolean
  deleteForEveryone: boolean
  react: boolean
}

export interface ActionContext {
  meId: string | null
  now: number
  /** Hours; 0 — no limit (GET /api/config). */
  editWindowHours: number
  deleteWindowHours: number
  /** Group permission to delete others' messages for everyone. */
  canDeleteOthers?: boolean
}

const withinHours = (createdAt: string, hours: number, now: number) =>
  hours === 0 || now - Date.parse(createdAt) < hours * 3_600_000

/** What the user may do with a message; the server checks the same rules again. */
export function messageActions(message: MessageDto, ctx: ActionContext): MessageActions {
  const system = message.type === 'system'
  const own = message.senderId === ctx.meId
  return {
    reply: !system,
    forward: !system,
    copy: message.text.length > 0,
    edit: own && !system && message.forwardFrom === null && withinHours(message.createdAt, ctx.editWindowHours, ctx.now),
    deleteForMe: true,
    deleteForEveryone:
      !system &&
      ((own && withinHours(message.createdAt, ctx.deleteWindowHours, ctx.now)) || ctx.canDeleteOthers === true),
    react: !system,
  }
}
