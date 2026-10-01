<script setup lang="ts">
import { useRealtimeSession } from '@/features/realtime/lib/useRealtimeSession'

// The only place the hub connection is tied to the session: connect on login, disconnect on logout.
useRealtimeSession()
</script>

<template>
  <RouterView />
</template>
