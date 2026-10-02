<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'

import BaseButton from '@/shared/ui/BaseButton.vue'
import BaseInput from '@/shared/ui/BaseInput.vue'
import { ApiError, NetworkError } from '@/shared/api/problem'
import * as authApi from '../api/auth.api'
import { useAuthStore } from '../model/auth.store'

/** An invitation code from the link a member shared: the form opens on registration with it. */
const props = defineProps<{ invite?: string | undefined }>()
const emit = defineEmits<{ success: [] }>()

const auth = useAuthStore()

type Mode = 'login' | 'register'
const mode = ref<Mode>(props.invite ? 'register' : 'login')

/** Who may register here; open until the server says otherwise (it decides anyway). */
const registration = ref<'open' | 'closed' | 'invite'>('open')

onMounted(async () => {
  try {
    const { mode: answer } = await authApi.getRegistration()
    if (answer === 'closed' || answer === 'invite') registration.value = answer
    if (answer === 'closed' && mode.value === 'register') mode.value = 'login'
  } catch {
    // The form stays as it is: registration itself will say if it is not possible.
  }
})

const form = reactive({
  usernameOrEmail: '',
  password: '',
  username: '',
  email: '',
  displayName: '',
  inviteCode: props.invite ?? '',
})

const isBusy = ref(false)
const errorText = ref('')

const submitLabel = computed(() => (mode.value === 'login' ? 'Войти' : 'Создать аккаунт'))

function switchMode(next: Mode): void {
  mode.value = next
  errorText.value = ''
}

async function submit(): Promise<void> {
  if (isBusy.value) return
  isBusy.value = true
  errorText.value = ''

  try {
    if (mode.value === 'login') {
      await auth.login({
        usernameOrEmail: form.usernameOrEmail.trim(),
        password: form.password,
      })
    } else {
      const displayName = form.displayName.trim()
      await auth.register({
        username: form.username.trim(),
        email: form.email.trim(),
        password: form.password,
        // Omitted rather than empty: the server length-checks it and defaults a missing one
        // to the username.
        ...(displayName ? { displayName } : {}),
        ...(registration.value === 'invite' ? { inviteCode: form.inviteCode.trim() } : {}),
      })
    }
    form.password = ''
    emit('success')
  } catch (error) {
    errorText.value = describe(error)
  } finally {
    isBusy.value = false
  }
}

function describe(error: unknown): string {
  if (error instanceof ApiError) {
    // Never reveal which of login or password was wrong.
    if (error.isUnauthorized) return 'Неверный логин или пароль'
    return error.userMessage
  }
  if (error instanceof NetworkError) return 'Сервер недоступен'
  return 'Что-то пошло не так'
}
</script>

<template>
  <form class="auth" @submit.prevent="submit">
    <h1 class="title">Basic<span>Chat</span></h1>

    <div class="tabs">
      <button
        type="button"
        :class="['tab', { active: mode === 'login' }]"
        @click="switchMode('login')"
      >
        Вход
      </button>
      <button
        v-if="registration !== 'closed'"
        type="button"
        :class="['tab', { active: mode === 'register' }]"
        @click="switchMode('register')"
      >
        Регистрация
      </button>
    </div>

    <template v-if="mode === 'login'">
      <BaseInput
        v-model="form.usernameOrEmail"
        label="Логин или email"
        autocomplete="username"
        :disabled="isBusy"
        required
      />
      <BaseInput
        v-model="form.password"
        label="Пароль"
        type="password"
        autocomplete="current-password"
        :disabled="isBusy"
        required
      />
    </template>

    <template v-else>
      <BaseInput
        v-model="form.username"
        label="Логин"
        autocomplete="username"
        :disabled="isBusy"
        required
      />
      <BaseInput
        v-model="form.email"
        label="Email"
        type="email"
        autocomplete="email"
        :disabled="isBusy"
        required
      />
      <BaseInput
        v-model="form.displayName"
        label="Отображаемое имя"
        autocomplete="nickname"
        :disabled="isBusy"
      />
      <BaseInput
        v-model="form.password"
        label="Пароль"
        type="password"
        autocomplete="new-password"
        :disabled="isBusy"
        required
      />
      <BaseInput
        v-if="registration === 'invite'"
        v-model="form.inviteCode"
        label="Код приглашения"
        autocomplete="off"
        :disabled="isBusy"
        required
      />
    </template>

    <!-- Text interpolation, never v-html: this shows server-provided messages. -->
    <p v-if="errorText" class="error" role="alert">{{ errorText }}</p>

    <BaseButton type="submit" :disabled="isBusy">
      {{ isBusy ? 'Подождите…' : submitLabel }}
    </BaseButton>
  </form>
</template>

<style scoped>
.auth {
  display: grid;
  gap: 14px;
  width: min(360px, 100%);
  padding: 26px;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--surface-solid);
}
.title {
  margin: 0;
  font-size: 22px;
  font-weight: 700;
  letter-spacing: 0.4px;
  text-align: center;
}
.title span {
  color: var(--accent);
}
.tabs {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 4px;
  padding: 3px;
  border-radius: var(--radius-sm);
  background: var(--bg);
}
.tab {
  padding: 7px;
  border: none;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-dim);
  font-size: 13px;
}
.tab.active {
  background: var(--accent-soft);
  color: var(--accent);
}
.error {
  margin: 0;
  color: var(--danger);
  font-size: 13px;
  white-space: pre-line;
}
</style>
