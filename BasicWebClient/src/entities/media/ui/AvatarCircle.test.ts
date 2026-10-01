import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { expect, it, vi } from 'vitest'

import AvatarCircle from './AvatarCircle.vue'

vi.mock('../api', () => ({
  getLinks: vi.fn(async (ids: string[]) => ({
    items: ids.map((id) => ({
      attachmentId: id,
      url: `https://s/${id}`,
      thumbnailUrl: `https://s/${id}.t`,
      expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
    })),
  })),
}))

const mountAvatar = (avatarId: string | null) =>
  mount(AvatarCircle, { props: { avatarId, initial: 'A' }, global: { plugins: [createPinia()] } })

it('no photo — the initial', () => {
  const wrapper = mountAvatar(null)
  expect(wrapper.find('img').exists()).toBe(false)
  expect(wrapper.text()).toBe('A')
})

it('a photo shows its preview; one that fails to load falls back to the initial', async () => {
  const wrapper = mountAvatar('av-1')
  await flushPromises()
  expect(wrapper.get('img').attributes('src')).toBe('https://s/av-1.t')

  await wrapper.get('img').trigger('error')
  expect(wrapper.find('img').exists()).toBe(false)
  expect(wrapper.text()).toBe('A')

  // A new photo is tried again.
  await wrapper.setProps({ avatarId: 'av-2' })
  await flushPromises()
  expect(wrapper.get('img').attributes('src')).toBe('https://s/av-2.t')
})
