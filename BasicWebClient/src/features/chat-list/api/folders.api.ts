import { http } from '@/shared/api/http'
import type { FolderDto, SaveFolderDto } from '@/shared/api/schema'

/** A new folder goes last. */
export function createFolder(body: SaveFolderDto): Promise<FolderDto> {
  return http.post<FolderDto>('/api/folders', body)
}

/** Only the given fields change; lists are replaced whole. */
export function updateFolder(folderId: string, body: SaveFolderDto): Promise<FolderDto> {
  return http.patch<FolderDto>(`/api/folders/${folderId}`, body)
}

/** The chats stay where they were. */
export function deleteFolder(folderId: string): Promise<void> {
  return http.delete<void>(`/api/folders/${folderId}`)
}

/** Exactly all folders, in the new order. */
export function reorderFolders(folderIds: string[]): Promise<FolderDto[]> {
  return http.put<FolderDto[]>('/api/folders/order', { folderIds })
}
