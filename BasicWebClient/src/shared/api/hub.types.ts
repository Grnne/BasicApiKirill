// Hub events: the names as the server sends them (BasicApi/Services/Events), the payloads generated
// from the contract. Journaled events come in GET /api/sync with the same type and payload.

import type {
  BlockListChangedDto,
  ChatDeletedDto,
  ChatListItemDto,
  ChatStateDto,
  ChatUpdatedDto,
  DraftUpdatedDto,
  FoldersDto,
  MemberRemovedDto,
  MembersAddedDto,
  MemberUpdatedDto,
  MessageDeletedDto,
  MessageDto,
  MessageReactionsDto,
  PinnedChatsDto,
  PrivacySettingsDto,
  PushNotificationDto,
  ReadStateDto,
  ReceiptDto,
  UserUpdatedDto,
} from './schema'

/** Events that are also written to the user's journal: one argument, the payload. */
export interface JournaledEvents {
  MessageCreated: MessageDto
  MessageUpdated: MessageDto
  MessageDeleted: MessageDeletedDto
  ReactionsChanged: MessageReactionsDto
  MessagesDelivered: ReceiptDto
  MessagesRead: ReceiptDto
  ReadStateChanged: ReadStateDto
  DraftUpdated: DraftUpdatedDto
  ChatCreated: ChatListItemDto
  ChatUpdated: ChatUpdatedDto
  ChatDeleted: ChatDeletedDto
  MemberAdded: MembersAddedDto
  MemberRemoved: MemberRemovedDto
  MemberUpdated: MemberUpdatedDto
  UserUpdated: UserUpdatedDto
  PrivacyUpdated: PrivacySettingsDto
  BlockListChanged: BlockListChangedDto
  PinnedChatsChanged: PinnedChatsDto
  ChatStateChanged: ChatStateDto
  FoldersChanged: FoldersDto
}

export type JournaledEventName = keyof JournaledEvents

export type HubEvents = {
  [K in JournaledEventName]: (payload: JournaledEvents[K]) => void
} & {
  /** Preview of the last message for the chat list; not journaled (MessageCreated is). */
  ChatListUpdated: (chatId: string, message: MessageDto) => void
  UserOnlineChanged: (userId: string, isOnline: boolean) => void
  TypingChanged: (chatId: string, userId: string, isTyping: boolean) => void
  /** What push would show if the client were closed (a reaction to the user's message); not journaled. */
  Notification: (notification: PushNotificationDto) => void
}

export type HubEventName = keyof HubEvents

export const JOURNALED_EVENT_NAMES: readonly JournaledEventName[] = [
  'MessageCreated',
  'MessageUpdated',
  'MessageDeleted',
  'ReactionsChanged',
  'MessagesDelivered',
  'MessagesRead',
  'ReadStateChanged',
  'DraftUpdated',
  'ChatCreated',
  'ChatUpdated',
  'ChatDeleted',
  'MemberAdded',
  'MemberRemoved',
  'MemberUpdated',
  'UserUpdated',
  'PrivacyUpdated',
  'BlockListChanged',
  'PinnedChatsChanged',
  'ChatStateChanged',
  'FoldersChanged',
]

/** Names are needed at runtime too: the connection subscribes by them. */
export const HUB_EVENT_NAMES: readonly HubEventName[] = [
  ...JOURNALED_EVENT_NAMES,
  'ChatListUpdated',
  'UserOnlineChanged',
  'TypingChanged',
  'Notification',
]

export type ConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'reconnecting'
