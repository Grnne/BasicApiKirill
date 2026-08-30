import { createApp } from 'vue'
import { createPinia } from 'pinia'

import App from './App.vue'
import { router } from './router'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { setAuthBridge } from '@/shared/api/http'
import './styles/theme.css'

const app = createApp(App)

// Порядок важен: Pinia до роутера, иначе навигационные guard'ы
// не смогут прочитать сторы (их ещё не будет).
const pinia = createPinia()
app.use(pinia)

// Связываем HTTP-клиент с auth-стором. Функции, а не значения: токен
// читается в момент запроса, поэтому после обновления берётся уже новый.
const auth = useAuthStore(pinia)
setAuthBridge({
  getAccessToken: () => auth.accessToken,
  refreshTokens: () => auth.refreshTokens(),
})

app.use(router)

app.mount('#app')
