<script setup lang="ts">
import { useRealtimeSession } from '@/features/realtime/lib/useRealtimeSession'

// Одно место, где соединение с хабом привязано к сессии:
// вошёл — подключились, вышел — отключились.
useRealtimeSession()
</script>

<template>
  <RouterView />
</template>
