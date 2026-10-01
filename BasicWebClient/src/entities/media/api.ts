import { http } from '@/shared/api/http'
import type { AttachmentDto, CreateUploadDto, MediaLinksDto, UploadTicketDto } from '@/shared/api/schema'

/** Step 1: what will be uploaded; answers where to put it (a signed link). */
export function createUpload(body: CreateUploadDto, signal?: AbortSignal): Promise<UploadTicketDto> {
  return http.post<UploadTicketDto>('/api/media/uploads', body, signal ? { signal } : {})
}

/** Step 3: the server reads what was uploaded and checks it. A repeat returns the same file. */
export function completeUpload(attachmentId: string, signal?: AbortSignal): Promise<AttachmentDto> {
  return http.post<AttachmentDto>(`/api/media/uploads/${attachmentId}/complete`, undefined, signal ? { signal } : {})
}

/** Signed links to show or download files; ones the user may not see are left out. Up to 100. */
export function getLinks(attachmentIds: string[]): Promise<MediaLinksDto> {
  return http.post<MediaLinksDto>('/api/media/links', { attachmentIds })
}
