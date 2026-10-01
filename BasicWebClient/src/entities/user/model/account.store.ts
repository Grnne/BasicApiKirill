import { ref, shallowRef } from 'vue'
import { defineStore } from 'pinia'

import type { OwnProfileResponseDto, PrivacySettingsDto } from '@/shared/api/schema'
import type { JournaledEventName, JournaledEvents } from '@/shared/api/hub.types'

/** The user's own profile, privacy settings and block list, kept current by sync. */
export const useAccountStore = defineStore('account', () => {
  const me = shallowRef<OwnProfileResponseDto | null>(null)
  const privacy = shallowRef<PrivacySettingsDto | null>(null)
  const blocked = ref<Set<string>>(new Set())

  function replaceAll(profile: OwnProfileResponseDto, settings: PrivacySettingsDto, blockedUserIds: string[]): void {
    me.value = profile
    privacy.value = settings
    blocked.value = new Set(blockedUserIds)
  }

  function apply<K extends JournaledEventName>(type: K, payload: JournaledEvents[K]): void {
    switch (type) {
      case 'UserUpdated': {
        const user = payload as JournaledEvents['UserUpdated']
        if (me.value?.userId === user.userId) {
          me.value = { ...me.value, displayName: user.displayName, username: user.username, avatarId: user.avatarId }
        }
        return
      }
      case 'PrivacyUpdated':
        privacy.value = payload as JournaledEvents['PrivacyUpdated']
        return
      case 'BlockListChanged': {
        const change = payload as JournaledEvents['BlockListChanged']
        const next = new Set(blocked.value)
        if (change.blocked) next.add(change.userId)
        else next.delete(change.userId)
        blocked.value = next
        return
      }
      default:
        return
    }
  }

  /** The answer to the user's own change; UserUpdated brings the same. */
  function setMe(profile: OwnProfileResponseDto): void {
    me.value = profile
  }

  function setPrivacy(settings: PrivacySettingsDto): void {
    privacy.value = settings
  }

  function isBlocked(userId: string | null | undefined): boolean {
    return !!userId && blocked.value.has(userId)
  }

  function reset(): void {
    me.value = null
    privacy.value = null
    blocked.value = new Set()
  }

  return { me, privacy, blocked, isBlocked, setMe, setPrivacy, replaceAll, apply, reset }
})
