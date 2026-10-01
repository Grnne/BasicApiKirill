// Message types: generated from the backend contract (shared/api/schema.d.ts).

import type {
  MessageDto,
  MessageDtoCursorPaginatedResponse,
  SearchMessagesResponseDto,
} from '@/shared/api/schema'

export type Message = MessageDto

/** A page going back in time: nextCursor leads to older messages. */
export type MessagePage = MessageDtoCursorPaginatedResponse

export type SearchMessagesResponse = SearchMessagesResponseDto
