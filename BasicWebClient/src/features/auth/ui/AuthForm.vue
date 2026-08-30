<script setup lang="ts">
import { computed, reactive, ref } from 'vue'

import BaseButton from '@/shared/ui/BaseButton.vue'
import BaseInput from '@/shared/ui/BaseInput.vue'
import { ApiError, NetworkError } from '@/shared/api/problem'
import { useAuthStore } from '../model/auth.store'

const emit = defineEmits<{ success: [] }>()

const auth = useAuthStore()

type Mode = 'login' | 'register'
const mode = ref<Mode>('login')

const form = reactive({
  usernameOrEmail: '',
  password: '',
  username: '',
  email: '',
  displayName: '',
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
        // Пустую строку не шлём: у поля на сервере есть ограничение длины,
        // а отсутствие значения он трактует как «взять username».
        ...(displayName ? { displayName } : {}),
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
    // 401 на логине — это всегда «не тот логин или пароль». Показывать
    // серверный текст не нужно, да и незачем уточнять, что именно не совпало.
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
    </template>

    <!-- Именно текстовая интерполяция, не v-html: сюда попадают сообщения сервера. -->
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
