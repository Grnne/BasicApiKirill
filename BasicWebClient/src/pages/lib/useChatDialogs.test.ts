import { nextTick, ref } from 'vue'
import { expect, it } from 'vitest'

import { useChatDialogs } from './useChatDialogs'

it('a chat that goes away takes its dialogs with it', async () => {
  // The bug: the group was deleted with its settings open; the next chat opened with them.
  const chatId = ref<string | null>('group-1')
  const dialogs = useChatDialogs(() => chatId.value)
  dialogs.editingGroup.value = true
  dialogs.managedMemberId.value = 'u-2'
  dialogs.showingAudit.value = true
  dialogs.addingMembers.value = true

  chatId.value = null
  await nextTick()
  chatId.value = 'group-2'
  await nextTick()

  expect(dialogs.editingGroup.value).toBe(false)
  expect(dialogs.managedMemberId.value).toBeNull()
  expect(dialogs.showingAudit.value).toBe(false)
  expect(dialogs.addingMembers.value).toBe(false)
})
