/**
 * Связывает жизнь соединения с авторизацией: залогинились — подключились,
 * вышли — отключились. Вызывается один раз, в корневом компоненте.
 *
 * Почему не внутри auth-стора: тогда auth зависел бы от hub, а hub уже зависит
 * от auth (ему нужен токен). Кольцо импортов и невозможность тестировать
 * авторизацию отдельно.
 */

import { watch } from 'vue'

import { useAuthStore } from '@/features/auth/model/auth.store'
import { useHubStore } from '@/shared/api/hub.store'

export function useRealtimeSession(): void {
  const auth = useAuthStore()
  const hub = useHubStore()

  watch(
    () => auth.isAuthenticated,
    (isAuthenticated) => {
      if (isAuthenticated) {
        void hub.start()
      } else {
        void hub.stop()
      }
    },
    // immediate: сессия могла восстановиться до того, как повесили наблюдение.
    { immediate: true },
  )
}
