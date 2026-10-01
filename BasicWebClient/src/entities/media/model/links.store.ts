// Signed links to files (POST /api/media/links), cached until shortly before they expire.
// Components ask for ids as they render; the asks of one tick go as one request (up to 100 ids).

import { ref } from 'vue'
import { defineStore } from 'pinia'

import type { MediaLinkDto } from '@/shared/api/schema'
import * as mediaApi from '../api'

const BATCH = 100
/** A link this close to expiry is fetched again: an <img> may load it a little later. */
const REFRESH_BEFORE_MS = 60_000

export function isFresh(link: MediaLinkDto | undefined, now: number): boolean {
  return !!link && Date.parse(link.expiresAt) - now > REFRESH_BEFORE_MS
}

export const useMediaLinksStore = defineStore('mediaLinks', () => {
  const links = ref<Record<string, MediaLinkDto>>({})
  const wanted = new Set<string>()
  const inFlight = new Set<string>()
  let scheduled = false

  async function flush(): Promise<void> {
    scheduled = false
    const ids = [...wanted].filter((id) => !inFlight.has(id))
    wanted.clear()
    for (let i = 0; i < ids.length; i += BATCH) {
      const batch = ids.slice(i, i + BATCH)
      batch.forEach((id) => inFlight.add(id))
      try {
        const answer = await mediaApi.getLinks(batch)
        const next = { ...links.value }
        for (const link of answer.items) next[link.attachmentId] = link
        links.value = next
      } catch {
        // The next render asks again.
      } finally {
        batch.forEach((id) => inFlight.delete(id))
      }
    }
  }

  /** The link if there is a fresh one; otherwise it is fetched and appears reactively. */
  function get(attachmentId: string): MediaLinkDto | null {
    const link = links.value[attachmentId]
    if (!isFresh(link, Date.now())) {
      wanted.add(attachmentId)
      if (!scheduled) {
        scheduled = true
        queueMicrotask(() => void flush())
      }
    }
    return link ?? null
  }

  function reset(): void {
    links.value = {}
    wanted.clear()
  }

  return { get, reset }
})
