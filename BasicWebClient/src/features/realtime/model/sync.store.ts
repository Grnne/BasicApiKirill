import { watch } from 'vue'
import { defineStore } from 'pinia'

import { useChatsStore } from '@/entities/chat/model/chats.store'
import { useConfigStore } from '@/entities/config/config.store'
import { useMediaLinksStore } from '@/entities/media/model/links.store'
import { useHistoryStore } from '@/entities/message/model/history.store'
import { useAccountStore } from '@/entities/user/model/account.store'
import { usePresenceStore } from '@/entities/user/presence.store'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { useHubStore } from '@/shared/api/hub.store'
import { JOURNALED_EVENT_NAMES, type JournaledEventName, type JournaledEvents } from '@/shared/api/hub.types'
import type { SyncStateDto } from '@/shared/api/schema'
import { syncApi } from '../api/sync.api'
import { SyncEngine } from './sync-engine'

/** Feeds the entity stores from the snapshot, live hub events and the journal. */
export const useSyncStore = defineStore('sync', () => {
  const auth = useAuthStore()
  const hub = useHubStore()
  const chats = useChatsStore()
  const history = useHistoryStore()
  const account = useAccountStore()
  const presence = usePresenceStore()
  const config = useConfigStore()
  const mediaLinks = useMediaLinksStore()

  const ctx = () => ({ meId: auth.user?.userId ?? '' })

  function snapshot(state: SyncStateDto): void {
    chats.replaceAll(state.chats, state.folders)
    account.replaceAll(state.me, state.privacy, state.blockedUserIds)
    history.invalidate()

    // Online changes from before the connection were missed: ask for the current statuses.
    const companions = state.chats.map((c) => c.companionId).filter((id): id is string => id !== null)
    void presence.loadStatuses(companions)
    void presence.loadTyping()
  }

  function apply<K extends JournaledEventName>(type: K, payload: JournaledEvents[K]): void {
    chats.apply(type, payload, ctx())
    history.apply(type, payload, ctx())
    account.apply(type, payload)
  }

  const engine = new SyncEngine(syncApi, { snapshot, apply })

  let unsubscribe: (() => void)[] = []
  let stopWatch: (() => void) | null = null

  function onVisibility(): void {
    if (document.visibilityState === 'visible') void engine.sync()
    else engine.flushAck()
  }

  function start(): void {
    if (stopWatch) return

    unsubscribe = JOURNALED_EVENT_NAMES.map((name) =>
      hub.on(name, ((payload: never) => engine.live(name, payload)) as never),
    )
    unsubscribe.push(
      hub.on('ChatListUpdated', (_chatId, message) => {
        if (!engine.hasSnapshot) return
        chats.preview(message, ctx())
        engine.scheduleCatchUp()
      }),
    )
    presence.subscribeToHub()

    void config.load()
    // The snapshot does not wait for the hub; every (re)connection may have missed events.
    void engine.sync()
    stopWatch = watch(
      () => hub.status,
      (status) => {
        if (status === 'connected') void engine.sync()
      },
    )
    document.addEventListener('visibilitychange', onVisibility)
  }

  /** Logout: nothing of the user may survive it. */
  function stop(): void {
    stopWatch?.()
    stopWatch = null
    for (const off of unsubscribe) off()
    unsubscribe = []
    document.removeEventListener('visibilitychange', onVisibility)

    engine.stop()
    chats.reset()
    history.reset()
    account.reset()
    presence.reset()
    config.reset()
    mediaLinks.reset()
  }

  return { start, stop, syncNow: () => engine.sync() }
})
