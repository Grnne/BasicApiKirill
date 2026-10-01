import { http } from '@/shared/api/http'
import type { OwnProfile } from '@/entities/user/types'

/** The name others see; the other devices and everyone sharing a chat get UserUpdated. */
export function updateProfile(displayName: string): Promise<OwnProfile> {
  return http.patch<OwnProfile>('/api/Users/me', { displayName })
}

/** A completed photo upload; null removes the avatar. Answers the own profile. */
export function setMyAvatar(attachmentId: string | null): Promise<OwnProfile> {
  return attachmentId
    ? http.put<OwnProfile>('/api/Users/me/avatar', { attachmentId })
    : http.delete<OwnProfile>('/api/Users/me/avatar')
}

/** Every other sign-in ends at once; this device stays signed in. */
export function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  return http.post<void>('/api/Auth/password', { currentPassword, newPassword })
}
