<script setup lang="ts">
import { computed, onMounted } from 'vue'

import { useNotifierStore } from '../model/notifier.store'
import { usePushStore } from '../model/push.store'

const push = usePushStore()
const notifier = useNotifierStore()
onMounted(() => void push.refresh())

const text = computed(() => {
  if (push.state === 'unsupported') return 'Этот браузер не умеет показывать уведомления.'
  if (push.state === 'denied') return 'Уведомления для сайта запрещены в настройках браузера — разрешите их там.'
  if (!push.notifying) {
    return 'Выключены. Включите — о новых сообщениях и реакциях на ваши скажет система, когда вкладка не на экране или закрыта.'
  }
  if (push.state === 'on') return 'Включены: когда вкладка не на экране или закрыта. Заглушённые чаты не уведомляют.'
  if (push.state === 'unavailable') {
    return 'Включены, пока сайт открыт. Когда вкладка закрыта — нет: на этом сервере push выключен.'
  }
  return 'Включены, пока сайт открыт; о сообщениях при закрытой вкладке — не удалось подписаться.'
})

const canEnable = computed(() => push.state !== 'unsupported' && push.state !== 'denied' && (!push.notifying || push.state === 'off'))
const canDisable = computed(() => push.notifying)

const sound = computed({
  get: () => notifier.sound,
  set: (on: boolean) => notifier.setSound(on),
})
</script>

<template>
  <section class="section">
    <h2 class="heading">Уведомления</h2>
    <p class="hint">{{ text }}</p>
    <div v-if="canEnable || canDisable">
      <button v-if="canEnable" type="button" class="button" :disabled="push.busy" @click="push.enable()">
        {{ push.busy ? 'Включаем…' : 'Включить уведомления' }}
      </button>
      <button v-if="canDisable" type="button" class="link danger" :disabled="push.busy" @click="push.disable()">Выключить</button>
    </div>
    <label class="check"><input v-model="sound" type="checkbox" /> Звук при новом сообщении</label>
    <p v-if="push.error" class="error">{{ push.error }}</p>
  </section>
</template>

<style scoped src="@/shared/ui/section.css"></style>
<style scoped>
.check {
  display: flex;
  gap: 8px;
  align-items: center;
  font-size: 13px;
  cursor: pointer;
}
</style>
