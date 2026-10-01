// Which chats a folder shows, from the chats the client already has. The same rules as the
// server's GET /api/folders/{id}/chats (ChatRepository.GetFolderChatsAsync).

import type { FolderDto } from '@/shared/api/schema'
import type { ChatListItem } from '../types'

/** In the folder: listed explicitly or pinned in it, or matching its types and not archived. */
function inFolder(folder: FolderDto, chat: ChatListItem): boolean {
  if (folder.chatIds.includes(chat.chatId) || folder.pinnedChatIds.includes(chat.chatId)) return true
  if (chat.archived) return false
  return (folder.includePrivate && chat.type === 'private') || (folder.includeGroups && chat.type === 'group')
}

/** Pinned in the folder first, in its order, then the most recently active. */
export function folderChats(folder: FolderDto, chats: readonly ChatListItem[]): ChatListItem[] {
  const pinnedAt = (chat: ChatListItem) => folder.pinnedChatIds.indexOf(chat.chatId)
  return chats
    .filter((c) => inFolder(folder, c))
    .filter((c) => !folder.onlyUnread || c.unreadCount > 0 || c.markedUnread || pinnedAt(c) !== -1)
    .sort((a, b) => {
      const pa = pinnedAt(a)
      const pb = pinnedAt(b)
      if (pa !== -1 || pb !== -1) {
        if (pa === -1) return 1
        if (pb === -1) return -1
        return pa - pb
      }
      return Date.parse(b.lastActivityAt) - Date.parse(a.lastActivityAt)
    })
}
