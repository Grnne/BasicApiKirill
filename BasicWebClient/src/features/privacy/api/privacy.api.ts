import { http } from '@/shared/api/http'
import type { PrivacySettingsDto } from '@/shared/api/schema'

export type PrivacyKey = 'lastSeen' | 'messages' | 'groupAdd'
export type PrivacyValue = 'everybody' | 'contacts' | 'nobody'

/** Only the given fields change; answers all of them. The other devices get PrivacyUpdated. */
export function updatePrivacy(patch: Partial<Record<PrivacyKey, PrivacyValue>>): Promise<PrivacySettingsDto> {
  return http.put<PrivacySettingsDto>('/api/Users/me/privacy', patch)
}
