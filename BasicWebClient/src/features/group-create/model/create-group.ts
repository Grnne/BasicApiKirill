import type { ChatListItem } from '@/entities/chat/types'
import type { AttachmentDto } from '@/shared/api/schema'
import { describeError } from '@/shared/api/problem'

export interface CreateGroupDeps {
  createGroup(title: string, memberIds: string[]): Promise<ChatListItem>
  uploadPhoto(file: File): Promise<AttachmentDto>
  setChatAvatar(chatId: string, attachmentId: string): Promise<void>
}

export interface CreateGroupResult {
  chat: ChatListItem
  /** The group exists; only its photo did not make it. */
  avatarError: string | null
}

/**
 * The group first, its photo after: a failed upload must not lose a group already created. The photo
 * reaches the list through ChatUpdated, like a change by any other member.
 */
export async function createGroupWithPhoto(
  input: { title: string; memberIds: string[]; photo: File | null },
  deps: CreateGroupDeps,
): Promise<CreateGroupResult> {
  const chat = await deps.createGroup(input.title.trim(), input.memberIds)
  if (!input.photo) return { chat, avatarError: null }

  try {
    const attachment = await deps.uploadPhoto(input.photo)
    await deps.setChatAvatar(chat.chatId, attachment.id)
    return { chat: { ...chat, avatarId: attachment.id }, avatarError: null }
  } catch (e) {
    return { chat, avatarError: describeError(e) }
  }
}
