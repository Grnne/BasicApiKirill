import { http } from '@/shared/api/http'
import type { MessagePage } from '@/entities/message/types'

export type GalleryFilter = 'media' | 'files' | 'voice' | 'links'

export const GALLERY_PAGE = 50

/** Messages of the chat with files of the kind or with links, newest first. */
export function getChatMedia(
  chatId: string,
  filter: GalleryFilter,
  cursor: string | null,
  signal?: AbortSignal,
): Promise<MessagePage> {
  return http.get<MessagePage>(`/api/chats/${chatId}/media`, {
    query: { filter, limit: GALLERY_PAGE, cursor: cursor ?? undefined },
    ...(signal ? { signal } : {}),
  })
}
