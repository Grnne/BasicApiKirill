import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'

import * as authApi from '../api/auth.api'
import InviteSection from './InviteSection.vue'

vi.mock('../api/auth.api', () => ({ getRegistration: vi.fn(), createInvite: vi.fn() }))

function mountSection() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/login', name: 'login', component: { template: '<div />' } }],
  })
  return mount(InviteSection, { global: { plugins: [router] } })
}

describe('inviting someone', () => {
  it('where registration is by invitation: a one-time link to the sign-up', async () => {
    vi.mocked(authApi.getRegistration).mockResolvedValue({ mode: 'invite' })
    vi.mocked(authApi.createInvite).mockResolvedValue({ code: 'abc', expiresAt: '2026-10-09T10:00:00Z' })

    const wrapper = mountSection()
    await flushPromises()
    await wrapper.find('button').trigger('click')
    await flushPromises()

    expect((wrapper.find('input').element as HTMLInputElement).value).toBe(`${window.location.origin}/login?invite=abc`)
  })

  it('anywhere else: nothing to show', async () => {
    vi.mocked(authApi.getRegistration).mockResolvedValue({ mode: 'open' })

    const wrapper = mountSection()
    await flushPromises()

    expect(wrapper.find('section').exists()).toBe(false)
  })
})
