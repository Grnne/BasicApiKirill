import { mount } from '@vue/test-utils'
import { beforeEach, expect, it } from 'vitest'
import { defineComponent, h, ref } from 'vue'

import ConfirmDialog from './ConfirmDialog.vue'

beforeEach(() => {
  document.body.innerHTML = ''
})

it('a dangerous question starts on "Отмена": a repeated Enter must not delete', () => {
  const wrapper = mount(ConfirmDialog, { props: { title: 'Удалить?', confirmLabel: 'Удалить', danger: true }, attachTo: document.body })
  expect(document.activeElement?.textContent?.trim()).toBe('Отмена')
  wrapper.unmount()
})

it('closed, it gives the focus back to what had it', async () => {
  const open = ref(false)
  const host = mount(
    defineComponent(() => () => [
      h('button', { id: 'opener', onClick: () => (open.value = true) }, 'open'),
      open.value ? h(ConfirmDialog, { title: 'T', confirmLabel: 'OK', onCancel: () => (open.value = false) }) : null,
    ]),
    { attachTo: document.body },
  )
  const opener = host.get('#opener').element as HTMLButtonElement
  opener.focus()
  await host.get('#opener').trigger('click')
  expect(document.activeElement).not.toBe(opener)

  await host.findComponent(ConfirmDialog).findAll('button')[0]!.trigger('click')

  expect(document.activeElement).toBe(opener)
})
