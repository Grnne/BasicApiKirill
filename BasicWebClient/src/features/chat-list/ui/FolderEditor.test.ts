import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { expect, it, vi } from 'vitest'

import { useChatsStore } from '@/entities/chat/model/chats.store'
import { chat } from '@/testing/fixtures'
import * as foldersApi from '../api/folders.api'
import FolderEditor from './FolderEditor.vue'

vi.mock('../api/folders.api', () => ({ updateFolder: vi.fn(), createFolder: vi.fn(), deleteFolder: vi.fn() }))

it('a chat taken out of the folder is unpinned in it too', async () => {
  // The bug: only chatIds went to the server, which keeps every pinned chat listed — the chat
  // stayed in the folder, pinned, after the user had unchecked it.
  const pinia = createPinia()
  setActivePinia(pinia)
  const folder = {
    id: 'f1', title: 'Работа', includePrivate: false, includeGroups: false, onlyUnread: false,
    chatIds: ['a', 'b'], pinnedChatIds: ['a', 'b'],
  }
  useChatsStore().replaceAll([chat({ chatId: 'a', title: 'A' }), chat({ chatId: 'b', title: 'B' })], [folder])
  vi.mocked(foldersApi.updateFolder).mockImplementation(async (_id, body) => ({ ...folder, ...body }) as never)
  const wrapper = mount(FolderEditor, { props: { folder }, global: { plugins: [pinia] } })

  await wrapper.findAll('.chats input')[0]!.trigger('change')
  await wrapper.get('form').trigger('submit')
  await flushPromises()

  expect(foldersApi.updateFolder).toHaveBeenCalledWith('f1', expect.objectContaining({ chatIds: ['b'], pinnedChatIds: ['b'] }))
})
