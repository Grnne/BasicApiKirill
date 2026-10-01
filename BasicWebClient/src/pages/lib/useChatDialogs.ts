import { ref, watch } from 'vue'

/**
 * The dialogs of the open chat. They belong to that chat: when it changes or goes away (the group
 * was deleted, the user removed from it) they close, or they would come up over the next chat.
 */
export function useChatDialogs(chatId: () => string | null) {
  const addingMembers = ref(false)
  const editingGroup = ref(false)
  const managedMemberId = ref<string | null>(null)
  const showingAudit = ref(false)

  function closeAll(): void {
    addingMembers.value = false
    editingGroup.value = false
    managedMemberId.value = null
    showingAudit.value = false
  }

  watch(chatId, closeAll)

  return { addingMembers, editingGroup, managedMemberId, showingAudit }
}
