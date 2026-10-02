import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import * as authApi from '../api/auth.api'
import AuthForm from './AuthForm.vue'

vi.mock('../api/auth.api', () => ({ getRegistration: vi.fn(), register: vi.fn(), login: vi.fn(), refresh: vi.fn() }))

beforeEach(() => {
  setActivePinia(createPinia())
  vi.mocked(authApi.register).mockReset()
})

const tabs = (wrapper: ReturnType<typeof mount>) => wrapper.findAll('.tab').map((t) => t.text())

describe('the sign-up form follows the server', () => {
  it('registration closed: only signing in is offered', async () => {
    vi.mocked(authApi.getRegistration).mockResolvedValue({ mode: 'closed' })

    const wrapper = mount(AuthForm)
    await flushPromises()

    expect(tabs(wrapper)).toEqual(['Вход'])
  })

  it('by invitation: the link opens registration with the code, and the code is sent', async () => {
    vi.mocked(authApi.getRegistration).mockResolvedValue({ mode: 'invite' })
    vi.mocked(authApi.register).mockRejectedValue(new Error('stop here'))

    const wrapper = mount(AuthForm, { props: { invite: 'code-1' } })
    await flushPromises()
    const inputs = wrapper.findAll('input')
    expect((inputs.at(-1)!.element as HTMLInputElement).value).toBe('code-1')
    await inputs[0]!.setValue('bob')
    await inputs[1]!.setValue('bob@test')
    await inputs[3]!.setValue('secret123')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(authApi.register).toHaveBeenCalledWith(expect.objectContaining({ username: 'bob', inviteCode: 'code-1' }))
  })

  it('open: no code is asked for or sent', async () => {
    vi.mocked(authApi.getRegistration).mockResolvedValue({ mode: 'open' })

    const wrapper = mount(AuthForm)
    await flushPromises()
    await wrapper.findAll('.tab')[1]!.trigger('click')

    expect(tabs(wrapper)).toEqual(['Вход', 'Регистрация'])
    expect(wrapper.text()).not.toContain('Код приглашения')
  })
})
