import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, expect, it, vi } from 'vitest'

import * as usersApi from '@/entities/user/api'
import { useAccountStore } from '@/entities/user/model/account.store'
import type { OwnProfileResponseDto } from '@/shared/api/schema'
import * as privacyApi from '../api/privacy.api'
import BlockedSection from './BlockedSection.vue'
import PrivacySection from './PrivacySection.vue'

vi.mock('../api/privacy.api', () => ({ updatePrivacy: vi.fn() }))
vi.mock('@/entities/user/api', () => ({ getBlocked: vi.fn(), unblockUser: vi.fn(async () => {}) }))
vi.mock('@/entities/media/api', () => ({ getLinks: vi.fn(async () => ({ items: [] })) }))

const me = { userId: 'me', username: 'me', email: 'me@test', displayName: 'Me', avatarId: null } as OwnProfileResponseDto
const bob = { userId: 'bob', username: 'bob', displayName: 'Bob', avatarId: null }

beforeEach(() => {
  setActivePinia(createPinia())
  vi.mocked(usersApi.getBlocked).mockReset()
})

it('never changed reads as "everybody"; a change sends only that field and shows the answer', async () => {
  const account = useAccountStore()
  account.replaceAll(me, { lastSeen: null, messages: 'contacts', groupAdd: null }, [])
  vi.mocked(privacyApi.updatePrivacy).mockResolvedValue({ lastSeen: 'nobody', messages: 'contacts', groupAdd: null })
  const wrapper = mount(PrivacySection)

  const selects = wrapper.findAll('select')
  expect((selects[0]!.element as HTMLSelectElement).value).toBe('everybody')
  expect((selects[1]!.element as HTMLSelectElement).value).toBe('contacts')

  await selects[0]!.setValue('nobody')
  await flushPromises()

  expect(privacyApi.updatePrivacy).toHaveBeenCalledWith({ lastSeen: 'nobody' })
  expect(account.privacy!.lastSeen).toBe('nobody')
})

it('the blocked list follows blocks made elsewhere; unblocking takes the person off', async () => {
  const account = useAccountStore()
  account.replaceAll(me, { lastSeen: null, messages: null, groupAdd: null }, [])
  vi.mocked(usersApi.getBlocked).mockResolvedValueOnce([]).mockResolvedValueOnce([bob]).mockResolvedValueOnce([])
  const wrapper = mount(BlockedSection)
  await flushPromises()
  expect(wrapper.text()).toContain('Никого')

  account.apply('BlockListChanged', { userId: 'bob', blocked: true })
  await flushPromises()
  expect(wrapper.text()).toContain('Bob')

  await wrapper.get('button.link').trigger('click')
  await flushPromises()
  expect(usersApi.unblockUser).toHaveBeenCalledWith('bob')
  expect(account.isBlocked('bob')).toBe(false)
  expect(wrapper.text()).toContain('Никого')
})
