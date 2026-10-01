<script setup lang="ts">
import { computed, onMounted } from 'vue'

import { usePushStore } from '../model/push.store'

const push = usePushStore()
onMounted(() => void push.refresh())

const TEXTS = {
  unsupported: 'Этот браузер не умеет показывать уведомления.',
  unavailable: 'На этом сервере уведомления выключены.',
  denied: 'Уведомления для сайта запрещены в настройках браузера — разрешите их там.',
  off: 'Сообщения, пришедшие, пока вкладка закрыта, покажет система. Заглушённые чаты не уведомляют.',
  on: 'Включены на этом устройстве. Заглушённые чаты не уведомляют.',
} as const

const text = computed(() => TEXTS[push.state])
</script>

<template>
  <section class="section">
    <h2 class="heading">Уведомления</h2>
    <p class="hint">{{ text }}</p>
    <div v-if="push.state === 'off' || push.state === 'on'">
      <button v-if="push.state === 'off'" type="button" class="button" :disabled="push.busy" @click="push.enable()">
        {{ push.busy ? 'Включаем…' : 'Включить уведомления' }}
      </button>
      <button v-else type="button" class="link danger" :disabled="push.busy" @click="push.disable()">Выключить</button>
    </div>
    <p v-if="push.error" class="error">{{ push.error }}</p>
  </section>
</template>

<style scoped src="@/shared/ui/section.css"></style>
