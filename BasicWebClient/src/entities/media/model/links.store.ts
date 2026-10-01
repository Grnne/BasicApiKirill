// Signed links to files (POST /api/media/links), cached until shortly before they expire.
// Components ask for ids as they render; the asks of one tick go as one request (up to 100 ids).

import { ref } from 'vue'
import { defineStore } from 'pinia'

import type { MediaLinkDto } from '@/shared/api/schema'
import * as mediaApi from '../api'

const BATCH = 100
/** A link this close to expiry is fetched again: an <img> may load it a little later. */
const REFRESH_BEFORE_MS = 60_000
/**
 * One id is asked for at most this often. Asks come from renders and an answer re-renders: an id
 * the server leaves out (a photo hidden by a block made after it was cached) or a link that looks
 * stale at once (a clock far ahead) would otherwise be asked for in a loop.
 */
const ASK_AGAIN_AFTER_MS = 30_000
/** Links expire while the page sits idle; renders that read them look again this often. */
const TICK_MS = 30_000

export function isFresh(link: MediaLinkDto | undefined, now: number): boolean {
  return !!link && Date.parse(link.expiresAt) - now > REFRESH_BEFORE_MS
}

export const useMediaLinksStore = defineStore('mediaLinks', () => {
  const links = ref<Record<string, MediaLinkDto>>({})
  const now = ref(Date.now())
  const wanted = new Set<string>()
  const inFlight = new Set<string>()
  const askedAt = new Map<string, number>()
  let scheduled = false
  let ticker: ReturnType<typeof setInterval> | undefined

  async function flush(): Promise<void> {
    scheduled = false
    const ids = [...wanted].filter((id) => !inFlight.has(id))
    wanted.clear()
    for (let i = 0; i < ids.length; i += BATCH) {
      const batch = ids.slice(i, i + BATCH)
      batch.forEach((id) => inFlight.add(id))
      try {
        const answer = await mediaApi.getLinks(batch)
        if (answer.items.length > 0) {
          const next = { ...links.value }
          for (const link of answer.items) next[link.attachmentId] = link
          links.value = next
        }
      } catch {
        // The next render after the pause asks again.
      } finally {
        batch.forEach((id) => inFlight.delete(id))
      }
    }
  }

  /** The link if there is a fresh one; otherwise it is fetched and appears reactively. */
  function get(attachmentId: string): MediaLinkDto | null {
    const link = links.value[attachmentId]
    const at = Date.now()
    // Read so that renders come back when the tick moves.
    void now.value
    if (!isFresh(link, at) && at - (askedAt.get(attachmentId) ?? -Infinity) >= ASK_AGAIN_AFTER_MS) {
      askedAt.set(attachmentId, at)
      wanted.add(attachmentId)
      ticker ??= setInterval(() => (now.value = Date.now()), TICK_MS)
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
    askedAt.clear()
    clearInterval(ticker)
    ticker = undefined
  }

  return { get, reset }
})
