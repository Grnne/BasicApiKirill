import { useRouter } from 'vue-router'

import * as authApi from '@/features/auth/api/auth.api'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { useChatListStore } from '@/features/chat-list/model/chat-list.store'
import { useMessagesStore } from '@/features/messages/model/messages.store'
import { usePushStore } from '@/features/push/model/push.store'
import { useSyncStore } from '@/features/realtime/model/sync.store'
import { useNoticesStore } from '@/shared/ui/notices.store'

/** Logging out from any page: nothing of the user may survive it, so stores go before the tokens. */
export function useLogout() {
  const auth = useAuthStore()
  const chatList = useChatListStore()
  const messages = useMessagesStore()
  const sync = useSyncStore()
  const router = useRouter()
  const notices = useNoticesStore()
  const push = usePushStore()

  async function logout(): Promise<void> {
    // While the token still works: the next user of this browser gets nothing of this one's.
    await push.forget()
    messages.reset()
    chatList.reset()
    sync.stop()
    await auth.logout()
    await router.replace({ name: 'login' })
  }

  /** Every device; throws if the server did not take it, and then this one stays signed in too. */
  async function logoutEverywhere(): Promise<void> {
    await push.forget()
    await authApi.logoutAll()
    await logout()
  }

  /** The session ended elsewhere (another device, another tab): the same clean-up, and say why. */
  async function afterSessionLost(): Promise<void> {
    auth.acknowledgeSessionLost()
    // The sign-in is gone and the server dropped its subscription; the browser keeps its own.
    void push.forget()
    messages.reset()
    chatList.reset()
    sync.stop()
    notices.push('Сеанс завершён — войдите снова', 'info')
    const current = router.currentRoute.value
    if (current.meta.requiresAuth) await router.replace({ name: 'login', query: { redirect: current.fullPath } })
  }

  return { logout, logoutEverywhere, afterSessionLost }
}
