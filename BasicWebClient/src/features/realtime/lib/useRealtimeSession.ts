/* Ties the hub connection to the session. Not in the auth store: the hub already depends on auth
   for the token, so that would be an import cycle. */

import { watch } from 'vue'

import { useAuthStore } from '@/features/auth/model/auth.store'
import { useHubStore } from '@/shared/api/hub.store'

export function useRealtimeSession(): void {
  const auth = useAuthStore()
  const hub = useHubStore()

  watch(
    () => auth.isAuthenticated,
    (isAuthenticated) => {
      if (isAuthenticated) {
        void hub.start()
      } else {
        void hub.stop()
      }
    },
    // The session may have been restored before the watcher was set up.
    { immediate: true },
  )
}
