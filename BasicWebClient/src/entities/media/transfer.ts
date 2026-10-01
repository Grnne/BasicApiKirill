// Browser plumbing of uploads: the PUT to a signed link (XHR for progress) and measuring a video.

/**
 * PUT the file to the storage's signed link. No API token: the signature is the permission, and
 * Content-Type is part of it, so it must be exactly the one the server answered.
 */
export function putFile(
  url: string,
  contentType: string,
  body: Blob,
  onProgress: (fraction: number) => void,
  signal: AbortSignal,
): Promise<void> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('PUT', url)
    xhr.setRequestHeader('Content-Type', contentType)
    xhr.upload.onprogress = (e) => {
      if (e.lengthComputable) onProgress(e.loaded / e.total)
    }
    xhr.onload = () => (xhr.status >= 200 && xhr.status < 300 ? resolve() : reject(new Error(`Upload failed: ${xhr.status}`)))
    xhr.onerror = () => reject(new Error('Upload failed: network'))
    xhr.onabort = () => reject(new DOMException('Aborted', 'AbortError'))
    signal.addEventListener('abort', () => xhr.abort(), { once: true })
    xhr.send(body)
  })
}

export interface VideoInfo {
  width: number | null
  height: number | null
  durationMs: number | null
  /** A frame for the preview (JPEG), or null when the browser cannot decode the video. */
  thumbnail: Blob | null
}

/** Frame size, duration and a frame near the start, read by the browser itself. */
export async function measureVideo(file: File): Promise<VideoInfo> {
  const url = URL.createObjectURL(file)
  const video = document.createElement('video')
  video.muted = true
  video.preload = 'metadata'
  video.src = url
  try {
    await new Promise<void>((resolve, reject) => {
      video.onloadeddata = () => resolve()
      video.onerror = () => reject(new Error('Cannot read the video'))
    })
    video.currentTime = Math.min(1, video.duration / 2 || 0)
    await new Promise<void>((resolve) => (video.onseeked = () => resolve()))

    const canvas = document.createElement('canvas')
    canvas.width = video.videoWidth
    canvas.height = video.videoHeight
    canvas.getContext('2d')?.drawImage(video, 0, 0)
    const thumbnail = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/jpeg', 0.8))
    return {
      width: video.videoWidth || null,
      height: video.videoHeight || null,
      durationMs: Number.isFinite(video.duration) ? Math.round(video.duration * 1000) : null,
      thumbnail,
    }
  } catch {
    return { width: null, height: null, durationMs: null, thumbnail: null }
  } finally {
    URL.revokeObjectURL(url)
  }
}
