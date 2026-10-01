/* Ties the hub connection and sync to the session. Not in the auth store: the hub already depends
   on auth for the token, so that would be an import cycle. The app passes whether someone is
   signed in: a feature does not read another feature's model. */

import { watch } from 'vue'

import { useHubStore } from '@/shared/api/hub.store'
import { useSyncStore } from '../model/sync.store'

export function useRealtimeSession(isAuthenticated: () => boolean): void {
  const hub = useHubStore()
  const sync = useSyncStore()

  watch(
    isAuthenticated,
    (signedIn) => {
      if (signedIn) {
        sync.start()
        void hub.start()
      } else {
        sync.stop()
        void hub.stop()
      }
    },
    // The session may have been restored before the watcher was set up.
    { immediate: true },
  )
}
