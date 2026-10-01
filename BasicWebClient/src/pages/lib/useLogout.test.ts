import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'

import { useChatsStore } from '@/entities/chat/model/chats.store'
import * as authApi from '@/features/auth/api/auth.api'
import { useChatListStore } from '@/features/chat-list/model/chat-list.store'
import { useNoticesStore } from '@/shared/ui/notices.store'
import { chat } from '@/testing/fixtures'
import { useLogout } from './useLogout'

const calls: string[] = []
vi.mock('@/features/realtime/model/sync.store', () => ({ useSyncStore: () => ({ stop: vi.fn() }) }))
vi.mock('@/shared/api/hub.store', () => ({
  useHubStore: () => ({ stop: async () => void calls.push('hub stopped'), joinChat: vi.fn(), leaveChat: vi.fn() }),
}))
vi.mock('@/features/push/model/push.store', () => ({ usePushStore: () => ({ forget: async () => void calls.push('push off') }) }))
vi.mock('@/features/auth/api/auth.api', () => ({
  logout: async () => void calls.push('logout'),
  logoutAll: vi.fn(),
}))

beforeEach(() => {
  setActivePinia(createPinia())
  calls.length = 0
})

function mountLogout() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'chat', component: { template: '<div />' } },
      { path: '/login', name: 'login', component: { template: '<div />' } },
    ],
  })
  let api!: ReturnType<typeof useLogout>
  mount(defineComponent({ setup: () => ((api = useLogout()), () => null) }), { global: { plugins: [router] } })
  return api
}

it('logout: the hub is down before the server ends the sign-in, so its drop is not taken for a lost session', async () => {
  await mountLogout().logout()

  expect(calls).toEqual(['push off', 'hub stopped', 'logout'])
})

it('a failed "log out everywhere" leaves this device as it was, notifications included', async () => {
  vi.mocked(authApi.logoutAll).mockRejectedValue(new Error('offline'))

  await expect(mountLogout().logoutEverywhere()).rejects.toThrow()

  expect(calls).toEqual([])
})

it('a session lost elsewhere: the open chat is forgotten, the user is told and taken to the login', async () => {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/chat', name: 'chat', component: { template: '<div />' }, meta: { requiresAuth: true } },
      { path: '/login', name: 'login', component: { template: '<div />' } },
    ],
  })
  await router.push('/chat')
  useChatsStore().replaceAll([chat({ chatId: 'c1' })], [])
  const list = useChatListStore()
  await list.select('c1')

  let api!: ReturnType<typeof useLogout>
  mount(defineComponent({ setup: () => ((api = useLogout()), () => null) }), { global: { plugins: [router] } })
  await api.afterSessionLost()
  await flushPromises()

  expect(list.selectedChatId).toBeNull()
  expect(router.currentRoute.value.name).toBe('login')
  expect(router.currentRoute.value.query.redirect).toBe('/chat')
  expect(useNoticesStore().items.map((n) => n.text)).toContain('Сеанс завершён — войдите снова')
})
