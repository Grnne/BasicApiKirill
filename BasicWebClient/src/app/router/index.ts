import { createRouter, createWebHistory } from 'vue-router'

import { useAuthStore } from '@/features/auth/model/auth.store'
import LoginPage from '@/pages/LoginPage.vue'
import ChatPage from '@/pages/ChatPage.vue'
import SettingsPage from '@/pages/SettingsPage.vue'

export const router = createRouter({
  // '/' in dev, '/client/' in prod (vite.config base).
  history: createWebHistory(import.meta.env.BASE_URL),

  routes: [
    { path: '/', redirect: '/chat' },
    { path: '/login', name: 'login', component: LoginPage },

    { path: '/chat', name: 'chat', component: ChatPage, meta: { requiresAuth: true } },
    { path: '/settings', name: 'settings', component: SettingsPage, meta: { requiresAuth: true } },

    { path: '/:pathMatch(.*)*', redirect: '/chat' },
  ],
})

/**
 * The session is restored here on page load: the access token lives only in memory, so without
 * waiting for the refresh every deep link would bounce to the login screen.
 */
router.beforeEach(async (to) => {
  const auth = useAuthStore()

  if (!auth.isSessionRestored) {
    await auth.restoreSession()
  }

  if (to.meta.requiresAuth && !auth.isAuthenticated) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }

  if (to.name === 'login' && auth.isAuthenticated) {
    return { name: 'chat' }
  }

  return true
})
