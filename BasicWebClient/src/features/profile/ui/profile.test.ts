import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useAccountStore } from '@/entities/user/model/account.store'
import type { OwnProfileResponseDto } from '@/shared/api/schema'
import { ApiError } from '@/shared/api/problem'
import { useNoticesStore } from '@/shared/ui/notices.store'
import * as profileApi from '../api/profile.api'
import PasswordSection from './PasswordSection.vue'
import ProfileSection from './ProfileSection.vue'

vi.mock('../api/profile.api', () => ({ updateProfile: vi.fn(), setMyAvatar: vi.fn(), changePassword: vi.fn() }))
vi.mock('@/entities/media/api', () => ({ getLinks: vi.fn(async () => ({ items: [] })), createUpload: vi.fn(), completeUpload: vi.fn() }))

const me = (displayName = 'Anna'): OwnProfileResponseDto =>
  ({ userId: 'me', username: 'anna', email: 'anna@test', displayName, avatarId: null }) as OwnProfileResponseDto
const privacy = { lastSeen: null, messages: null, groupAdd: null }

beforeEach(() => {
  setActivePinia(createPinia())
  vi.mocked(profileApi.updateProfile).mockReset()
  vi.mocked(profileApi.changePassword).mockReset()
})

describe('profile', () => {
  it('a new name is saved trimmed and shown at once', async () => {
    const account = useAccountStore()
    account.replaceAll(me(), privacy, [])
    vi.mocked(profileApi.updateProfile).mockResolvedValue(me('Anna K'))
    const wrapper = mount(ProfileSection)

    await wrapper.get('input[type=text]').setValue('  Anna K ')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(profileApi.updateProfile).toHaveBeenCalledWith('Anna K')
    expect(account.me!.displayName).toBe('Anna K')
  })

  it('a name being typed is not overwritten by the profile coming later', async () => {
    const account = useAccountStore()
    account.replaceAll(me(), privacy, [])
    const wrapper = mount(ProfileSection)
    await wrapper.get('input[type=text]').setValue('Ann')

    account.apply('UserUpdated', { userId: 'me', username: 'anna', displayName: 'Anna from phone', avatarId: null })
    await flushPromises()

    expect((wrapper.get('input[type=text]').element as HTMLInputElement).value).toBe('Ann')
  })
})

describe('password', () => {
  async function fill(wrapper: ReturnType<typeof mount>, current: string, next: string, repeat: string) {
    const [a, b, c] = wrapper.findAll('input[type=password]')
    await a!.setValue(current)
    await b!.setValue(next)
    await c!.setValue(repeat)
  }

  it('short or mismatched — not sent, and says why', async () => {
    const wrapper = mount(PasswordSection)
    await fill(wrapper, 'old-pass', 'abc', 'abc')
    expect(wrapper.text()).toContain('не короче 6')
    await fill(wrapper, 'old-pass', 'abcdef', 'abcdeg')
    expect(wrapper.text()).toContain('Пароли не совпадают')
    expect(wrapper.get('button[type=submit]').attributes('disabled')).toBeDefined()
  })

  it('changed: the fields are cleared and the other devices are mentioned; a wrong one says so', async () => {
    vi.mocked(profileApi.changePassword).mockResolvedValueOnce(undefined)
    const wrapper = mount(PasswordSection)
    await fill(wrapper, 'old-pass', 'new-pass', 'new-pass')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(profileApi.changePassword).toHaveBeenCalledWith('old-pass', 'new-pass')
    expect(wrapper.findAll('input[type=password]').every((i) => (i.element as HTMLInputElement).value === '')).toBe(true)
    expect(useNoticesStore().items[0]!.text).toContain('других устройствах')
    // The server signed the other devices out: the list on the page must not still show them.
    expect(wrapper.emitted('changed')).toHaveLength(1)

    vi.mocked(profileApi.changePassword).mockRejectedValueOnce(
      new ApiError(400, { title: 'Bad', status: 400, errorCode: 'WRONG_PASSWORD' } as never),
    )
    await fill(wrapper, 'bad-pass', 'new-pass', 'new-pass')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(wrapper.text()).toContain('Неверный текущий пароль')
  })
})
