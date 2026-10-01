<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'

import DevicesSection from '@/features/devices/ui/DevicesSection.vue'
import PasswordSection from '@/features/profile/ui/PasswordSection.vue'
import ProfileSection from '@/features/profile/ui/ProfileSection.vue'
import BlockedSection from '@/features/privacy/ui/BlockedSection.vue'
import PushSection from '@/features/push/ui/PushSection.vue'
import PrivacySection from '@/features/privacy/ui/PrivacySection.vue'
import { describeError } from '@/shared/api/problem'
import { useNoticesStore } from '@/shared/ui/notices.store'
import { useLogout } from './lib/useLogout'

const router = useRouter()
const notices = useNoticesStore()
const { logout, logoutEverywhere } = useLogout()
/** A password change signs the other devices out: the list is loaded again. */
const devicesVersion = ref(0)

async function onSignOutEverywhere(): Promise<void> {
  try {
    await logoutEverywhere()
  } catch (e) {
    notices.push(describeError(e))
  }
}
</script>

<template>
  <div class="page">
    <header class="bar">
      <button type="button" class="back" @click="router.push({ name: 'chat' })">← К чатам</button>
      <h1 class="title">Настройки</h1>
    </header>

    <main class="content">
      <ProfileSection />
      <PasswordSection @changed="devicesVersion += 1" />
      <PushSection />
      <PrivacySection />
      <BlockedSection />
      <DevicesSection :key="devicesVersion" @sign-out-here="logout" @sign-out-everywhere="onSignOutEverywhere" />
    </main>
  </div>
</template>

<style scoped>
.page {
  display: grid;
  grid-template-rows: auto 1fr;
  height: 100%;
  overflow: hidden;
}
.bar {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 10px 16px;
  border-bottom: 1px solid var(--border);
  background: var(--surface-solid);
}
.back {
  padding: 4px 10px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-dim);
}
.title {
  margin: 0;
  font-size: 16px;
}
.content {
  display: grid;
  align-content: start;
  gap: 16px;
  width: min(640px, 100%);
  margin: 0 auto;
  padding: 16px;
  overflow-y: auto;
}
</style>
