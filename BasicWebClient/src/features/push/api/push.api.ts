import { http } from '@/shared/api/http'
import type { PushConfigDto } from '@/shared/api/schema'

/** Whether the server sends push at all, and its key to subscribe with. */
export function getPushConfig(): Promise<PushConfigDto> {
  return http.get<PushConfigDto>('/api/push/config')
}

/** The browser's PushSubscription.toJSON(); one per sign-in, a new one replaces the old. */
export function saveSubscription(subscription: PushSubscriptionJSON): Promise<void> {
  return http.put<void>('/api/push/subscription', subscription)
}

/** No more push to this sign-in; without a subscription — fine too. */
export function deleteSubscription(): Promise<void> {
  return http.delete<void>('/api/push/subscription')
}
