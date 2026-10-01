import { createApp } from 'vue'
import { createPinia } from 'pinia'

import App from './App.vue'
import { router } from './router'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { setAuthBridge } from '@/shared/api/http'
import './styles/theme.css'

const app = createApp(App)

// Pinia must be installed before the router: navigation guards read stores.
const pinia = createPinia()
app.use(pinia)

// Getters, not values: the token is read per request, so a refreshed token is picked up.
const auth = useAuthStore(pinia)
setAuthBridge({
  getAccessToken: () => auth.accessToken,
  refreshTokens: () => auth.refreshTokens(),
})

app.use(router)

app.mount('#app')
