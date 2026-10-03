<script setup lang="ts">
import { watch } from 'vue'

import { useRouter } from 'vue-router'

import { useAuthStore } from '@/features/auth/model/auth.store'
import { useChatListStore } from '@/features/chat-list/model/chat-list.store'
import { useNotifierStore } from '@/features/push/model/notifier.store'
import { usePushStore } from '@/features/push/model/push.store'
import { useRealtimeSession } from '@/features/realtime/lib/useRealtimeSession'
import { useLogout } from '@/pages/lib/useLogout'
import NoticeList from '@/shared/ui/NoticeList.vue'

const auth = useAuthStore()

// The only place the hub connection is tied to the session: connect on login, disconnect on logout.
useRealtimeSession(() => auth.isAuthenticated)

// Signed out from elsewhere: the page would stay with no data and no way back but F5.
const { afterSessionLost } = useLogout()
watch(
  () => auth.sessionLost,
  (lost) => {
    if (lost) void afterSessionLost()
  },
)

// Notifications: a push subscription this browser holds follows whoever signs in; the open client
// shows its own while signed in. On by default: the first click after sign-in asks the browser.
const push = usePushStore()
const notifier = useNotifierStore()
const chatList = useChatListStore()
watch(
  () => auth.isAuthenticated,
  (signedIn) => {
    if (signedIn) {
      void push.resync()
      notifier.start(() => chatList.selectedChatId)
    } else {
      notifier.stop()
    }
  },
  { immediate: true },
)
document.addEventListener(
  'click',
  () => {
    if (auth.isAuthenticated) void push.askOnce()
  },
  true,
)

// A click on a notification while the client is open: the service worker asks to open the chat.
const router = useRouter()
navigator.serviceWorker?.addEventListener('message', (event: MessageEvent) => {
  const data = event.data as { type?: unknown; chatId?: unknown } | null
  if (data?.type === 'open-chat' && typeof data.chatId === 'string') {
    void router.push({ name: 'chat', query: { open: data.chatId } })
  }
})
</script>

<template>
  <RouterView />
  <NoticeList />
</template>
