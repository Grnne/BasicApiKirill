<script setup lang="ts">
import { watch } from 'vue'

import { useAuthStore } from '@/features/auth/model/auth.store'
import { useRealtimeSession } from '@/features/realtime/lib/useRealtimeSession'
import { useLogout } from '@/pages/lib/useLogout'
import NoticeList from '@/shared/ui/NoticeList.vue'

// The only place the hub connection is tied to the session: connect on login, disconnect on logout.
useRealtimeSession()

// Signed out from elsewhere: the page would stay with no data and no way back but F5.
const auth = useAuthStore()
const { afterSessionLost } = useLogout()
watch(
  () => auth.sessionLost,
  (lost) => {
    if (lost) void afterSessionLost()
  },
)
</script>

<template>
  <RouterView />
  <NoticeList />
</template>
