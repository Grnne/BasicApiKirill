<script setup lang="ts">
import { computed, onMounted, ref, shallowRef } from 'vue'

import { describeError } from '@/shared/api/problem'
import type { DeviceDto } from '@/shared/api/schema'
import { formatDay, formatTime } from '@/shared/lib/date'
import ConfirmDialog from '@/shared/ui/ConfirmDialog.vue'
import { getDevices, signOutDevice } from '../api/devices.api'
import { deviceName } from '../lib/user-agent'

/** This device and "everywhere" sign the user out here too: the page does it, with the stores. */
const emit = defineEmits<{ signOutHere: []; signOutEverywhere: [] }>()

const devices = shallowRef<DeviceDto[]>([])
const loading = ref(true)
const error = ref<string | null>(null)

// This one first, then by the last activity.
const sorted = computed(() =>
  [...devices.value].sort(
    (a, b) => Number(b.isCurrent) - Number(a.isCurrent) || Date.parse(b.lastActiveAt) - Date.parse(a.lastActiveAt),
  ),
)
const others = computed(() => devices.value.filter((d) => !d.isCurrent).length)

async function load(): Promise<void> {
  try {
    devices.value = (await getDevices()).items
    error.value = null
  } catch (e) {
    error.value = describeError(e)
  } finally {
    loading.value = false
  }
}
onMounted(load)

const busy = ref<string | null>(null)

async function signOut(device: DeviceDto): Promise<void> {
  if (device.isCurrent) {
    emit('signOutHere')
    return
  }
  busy.value = device.id
  try {
    await signOutDevice(device.id)
    devices.value = devices.value.filter((d) => d.id !== device.id)
  } catch (e) {
    error.value = describeError(e)
    void load()
  } finally {
    busy.value = null
  }
}

const confirmingAll = ref(false)
const when = (iso: string) => `${formatDay(iso)} ${formatTime(iso)}`
</script>

<template>
  <section class="section">
    <h2 class="heading">Устройства</h2>
    <p v-if="loading" class="hint">загрузка…</p>
    <ul v-else class="list">
      <li v-for="d in sorted" :key="d.id" class="device">
        <span class="info">
          <span class="name">
            {{ deviceName(d.userAgent) }}
            <span v-if="d.isCurrent" class="current">это устройство</span>
            <span v-if="d.pushEnabled" class="push" title="Получает уведомления">🔔</span>
          </span>
          <span class="hint">Вход {{ when(d.signedInAt) }} · активность {{ when(d.lastActiveAt) }}</span>
        </span>
        <button type="button" class="link danger" :disabled="busy === d.id" @click="signOut(d)">Выйти</button>
      </li>
    </ul>
    <p v-if="error" class="error">{{ error }}</p>
    <button v-if="others > 0" type="button" class="link danger all" @click="confirmingAll = true">
      Выйти на всех устройствах
    </button>

    <ConfirmDialog
      v-if="confirmingAll"
      title="Выйти на всех устройствах?"
      text="Везде, и здесь тоже, придётся войти заново."
      confirm-label="Выйти везде"
      danger
      @confirm="confirmingAll = false; emit('signOutEverywhere')"
      @cancel="confirmingAll = false"
    />
  </section>
</template>

<style scoped src="@/shared/ui/section.css"></style>

<style scoped>
.list {
  display: grid;
  gap: 10px;
  margin: 0;
  padding: 0;
  list-style: none;
}
.device {
  display: flex;
  align-items: center;
  gap: 10px;
}
.info {
  display: grid;
  flex: 1;
  min-width: 0;
}
.current {
  margin-left: 6px;
  padding: 1px 6px;
  border-radius: 8px;
  background: var(--accent-soft);
  color: var(--accent);
  font-size: 11px;
}
.push {
  margin-left: 4px;
  font-size: 12px;
}
.all {
  justify-self: start;
}
</style>
