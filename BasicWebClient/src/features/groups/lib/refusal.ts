import { ApiError, describeError } from '@/shared/api/problem'

/**
 * Why adding people failed. A privacy refusal names who refused (the server sends their ids):
 * otherwise the user would find them by trial and error among everyone picked.
 */
export function describeAddError(error: unknown, picked: readonly { userId: string; displayName: string }[]): string {
  const ids = error instanceof ApiError && error.code === 'PRIVACY_RESTRICTED' ? error.problem?.userIds ?? [] : []
  const names = picked.filter((u) => ids.includes(u.userId)).map((u) => u.displayName)
  if (names.length === 0) return describeError(error)
  return `Не разрешают добавлять себя в группы: ${names.join(', ')}. Уберите их и попробуйте снова`
}
