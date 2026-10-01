import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, expect, it, vi } from 'vitest'

import type { DeviceDto } from '@/shared/api/schema'
import * as devicesApi from '../api/devices.api'
import DevicesSection from './DevicesSection.vue'

vi.mock('../api/devices.api', () => ({ getDevices: vi.fn(), signOutDevice: vi.fn(async () => {}) }))

const device = (id: string, extra: Partial<DeviceDto> = {}): DeviceDto => ({
  id, isCurrent: false, pushEnabled: false, userAgent: 'Mozilla/5.0 (Windows NT 10.0) Chrome/140.0 Safari/537.36',
  signedInAt: '2026-09-01T10:00:00Z', lastActiveAt: '2026-10-01T10:00:00Z', ...extra,
})

beforeEach(() => {
  vi.mocked(devicesApi.getDevices).mockResolvedValue({
    items: [device('phone', { lastActiveAt: '2026-10-01T11:00:00Z' }), device('here', { isCurrent: true })],
  })
})

it('this device first; another one is signed out by the server, this one by the page', async () => {
  const wrapper = mount(DevicesSection)
  await flushPromises()

  const rows = wrapper.findAll('.device')
  expect(rows[0]!.text()).toContain('это устройство')

  await rows[1]!.get('button').trigger('click')
  await flushPromises()
  expect(devicesApi.signOutDevice).toHaveBeenCalledWith('phone')
  expect(wrapper.findAll('.device')).toHaveLength(1)

  await wrapper.get('.device button').trigger('click')
  expect(wrapper.emitted('signOutHere')).toHaveLength(1)
  expect(devicesApi.signOutDevice).toHaveBeenCalledTimes(1)
})

it('"everywhere" asks first, then leaves it to the page', async () => {
  const wrapper = mount(DevicesSection, { attachTo: document.body })
  await flushPromises()

  await wrapper.get('button.all').trigger('click')
  const confirm = [...document.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Выйти везде')!
  confirm.click()
  await flushPromises()

  expect(wrapper.emitted('signOutEverywhere')).toHaveLength(1)
  wrapper.unmount()
})
