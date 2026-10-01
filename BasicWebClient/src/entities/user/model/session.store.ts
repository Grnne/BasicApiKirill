import { computed, shallowRef } from 'vue'
import { defineStore } from 'pinia'

import type { OwnProfile } from '../types'

/**
 * Who is signed in on this tab. The auth feature sets it; every other feature reads it from here
 * instead of from the auth feature's model.
 */
export const useSessionStore = defineStore('session', () => {
  const user = shallowRef<OwnProfile | null>(null)
  const userId = computed(() => user.value?.userId ?? null)
  return { user, userId }
})
