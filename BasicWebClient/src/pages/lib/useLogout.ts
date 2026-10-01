import { useRouter } from 'vue-router'

import * as authApi from '@/features/auth/api/auth.api'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { useChatListStore } from '@/features/chat-list/model/chat-list.store'
import { useMessagesStore } from '@/features/messages/model/messages.store'
import { useSyncStore } from '@/features/realtime/model/sync.store'

/** Logging out from any page: nothing of the user may survive it, so stores go before the tokens. */
export function useLogout() {
  const auth = useAuthStore()
  const chatList = useChatListStore()
  const messages = useMessagesStore()
  const sync = useSyncStore()
  const router = useRouter()

  async function logout(): Promise<void> {
    messages.reset()
    chatList.reset()
    sync.stop()
    await auth.logout()
    await router.replace({ name: 'login' })
  }

  /** Every device; throws if the server did not take it, and then this one stays signed in too. */
  async function logoutEverywhere(): Promise<void> {
    await authApi.logoutAll()
    await logout()
  }

  return { logout, logoutEverywhere }
}
