import { shallowRef } from 'vue'
import { defineStore } from 'pinia'

import { http } from '@/shared/api/http'
import type { ClientConfigDto } from '@/shared/api/schema'

/** The server defaults: used until /api/config answers, so the UI never waits for it. */
export const DEFAULT_CONFIG: ClientConfigDto = {
  messages: {
    maxLength: 4096,
    maxAttachments: 10,
    reactions: ['👍', '❤️', '😂', '😮', '😢', '🙏', '👎', '🔥', '🎉'],
    editWindowHours: 48,
    deleteWindowHours: 48,
  },
  media: {
    enabled: false,
    maxFileSize: 100 * 1024 * 1024,
    maxPhotoSize: 20 * 1024 * 1024,
    maxPngGifPhotoPixels: 12_000_000,
    retentionDays: 0,
  },
  groups: { maxMembers: 500, maxTitleLength: 128 },
  push: { enabled: false },
}

/** The instance's limits and switches (GET /api/config). */
export const useConfigStore = defineStore('config', () => {
  const config = shallowRef<ClientConfigDto>(DEFAULT_CONFIG)

  async function load(): Promise<void> {
    try {
      config.value = await http.get<ClientConfigDto>('/api/config')
    } catch {
      // Defaults stay; the server still enforces the real limits.
    }
  }

  function reset(): void {
    config.value = DEFAULT_CONFIG
  }

  return { config, load, reset }
})
