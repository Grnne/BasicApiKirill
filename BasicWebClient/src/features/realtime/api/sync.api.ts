import { http } from '@/shared/api/http'
import type { SyncDifferenceDto, SyncStateDto } from '@/shared/api/schema'
import type { SyncApi } from '../model/sync-engine'

export const syncApi: SyncApi = {
  getState: () => http.get<SyncStateDto>('/api/sync/state'),
  getDifference: (since, limit) => http.get<SyncDifferenceDto>('/api/sync', { query: { since, limit } }),
  ack: (pts) => http.post<void>('/api/sync/ack', { pts }),
}
