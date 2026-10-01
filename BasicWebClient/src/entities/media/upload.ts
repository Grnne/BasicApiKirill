// One file through the three upload steps: ticket, PUT to the signed link, complete.

import type { AttachmentDto, CreateUploadDto, UploadTicketDto } from '@/shared/api/schema'
import type { UploadKind } from './lib'
import type { VideoInfo } from './transfer'

export interface UploadDeps {
  createUpload(body: CreateUploadDto, signal: AbortSignal): Promise<UploadTicketDto>
  completeUpload(attachmentId: string, signal: AbortSignal): Promise<AttachmentDto>
  putFile(url: string, contentType: string, body: Blob, onProgress: (f: number) => void, signal: AbortSignal): Promise<void>
  measureVideo(file: File): Promise<VideoInfo>
}

export async function uploadFile(
  file: File,
  kind: UploadKind,
  deps: UploadDeps,
  onProgress: (fraction: number) => void,
  signal: AbortSignal,
): Promise<AttachmentDto> {
  const video = kind === 'video' ? await deps.measureVideo(file) : null
  const ticket = await deps.createUpload(
    {
      kind,
      fileName: file.name,
      mimeType: file.type || 'application/octet-stream',
      size: file.size,
      ...(video ? { width: video.width, height: video.height, durationMs: video.durationMs, withThumbnail: !!video.thumbnail } : {}),
    },
    signal,
  )

  await deps.putFile(ticket.uploadUrl, ticket.contentType, file, onProgress, signal)
  if (video?.thumbnail && ticket.thumbnailUploadUrl && ticket.thumbnailContentType) {
    await deps.putFile(ticket.thumbnailUploadUrl, ticket.thumbnailContentType, video.thumbnail, () => {}, signal)
  }
  onProgress(1)
  return deps.completeUpload(ticket.attachmentId, signal)
}
