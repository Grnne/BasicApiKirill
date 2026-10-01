// Chat types: generated from the backend contract (shared/api/schema.d.ts).

import type {
  ChatDetailDto,
  ChatListItemDto,
  ChatParticipantDto,
  SearchChatsResponseDto,
} from '@/shared/api/schema'

export type ChatType = 'private' | 'group' | 'saved'

/** A row of the chat list; companion* describe the other side of a private chat. */
export type ChatListItem = ChatListItemDto

export type ChatParticipant = ChatParticipantDto

/** GET /api/chats/{id} */
export type ChatDetail = ChatDetailDto

export type SearchChatsResponse = SearchChatsResponseDto
