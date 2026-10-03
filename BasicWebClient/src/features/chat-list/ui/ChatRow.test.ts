import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { expect, it } from 'vitest'

import type { ChatListItem } from '@/entities/chat/types'
import { chat } from '@/testing/fixtures'
import ChatRow from './ChatRow.vue'

const render = (props: { chat: ChatListItem; pinned?: boolean }) =>
  mount(ChatRow, { props: { active: false, ...props }, global: { plugins: [createPinia()] } })

it('marks new reactions to my messages', () => {
  expect(render({ chat: chat({ unreadReactionCount: 2 }) }).find('.badge.reaction').exists()).toBe(true)
  expect(render({ chat: chat() }).find('.badge.reaction').exists()).toBe(false)
})

it('shows the global pin unless told where the list is pinned', () => {
  expect(render({ chat: chat({ pinnedPosition: 1 }) }).find('.pin').exists()).toBe(true)
  expect(render({ chat: chat({ pinnedPosition: 1 }), pinned: false }).find('.pin').exists()).toBe(false)
  expect(render({ chat: chat(), pinned: true }).find('.pin').exists()).toBe(true)
})
