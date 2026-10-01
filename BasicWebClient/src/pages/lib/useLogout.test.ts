import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'

import { useChatsStore } from '@/entities/chat/model/chats.store'
import { useChatListStore } from '@/features/chat-list/model/chat-list.store'
import { useNoticesStore } from '@/shared/ui/notices.store'
import { chat } from '@/testing/fixtures'
import { useLogout } from './useLogout'

vi.mock('@/features/realtime/model/sync.store', () => ({ useSyncStore: () => ({ stop: vi.fn() }) }))

beforeEach(() => setActivePinia(createPinia()))

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
