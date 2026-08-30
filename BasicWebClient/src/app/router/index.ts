import { createRouter, createWebHistory } from 'vue-router'

import { useAuthStore } from '@/features/auth/model/auth.store'
import LoginPage from '@/pages/LoginPage.vue'
import ChatPage from '@/pages/ChatPage.vue'

export const router = createRouter({
  // BASE_URL берётся из vite.config (base). В деве это '/', в проде '/client/'.
  history: createWebHistory(import.meta.env.BASE_URL),

  routes: [
    { path: '/', redirect: '/chat' },
    { path: '/login', name: 'login', component: LoginPage },

    // meta.requiresAuth читает guard, который появится вместе с auth-стором.
    { path: '/chat', name: 'chat', component: ChatPage, meta: { requiresAuth: true } },

    // Неизвестный путь — не 404-страница, а возврат в приложение.
    { path: '/:pathMatch(.*)*', redirect: '/chat' },
  ],
})

/**
 * Единственный guard приложения.
 *
 * Он же место, где сессия восстанавливается при загрузке страницы: до первой
 * навигации access-токена в памяти нет, и без ожидания любой заход по прямой
 * ссылке выкидывал бы на экран входа.
 */
router.beforeEach(async (to) => {
  const auth = useAuthStore()

  if (!auth.isSessionRestored) {
    await auth.restoreSession()
  }

  if (to.meta.requiresAuth && !auth.isAuthenticated) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }

  // Залогиненному на экране входа делать нечего.
  if (to.name === 'login' && auth.isAuthenticated) {
    return { name: 'chat' }
  }

  return true
})
