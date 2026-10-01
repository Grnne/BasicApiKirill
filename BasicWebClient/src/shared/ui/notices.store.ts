import { ref } from 'vue'
import { defineStore } from 'pinia'

export interface Notice {
  id: number
  text: string
  kind: 'error' | 'info'
}

const LIFETIME_MS = 6_000

/** Short messages for the user that go away by themselves (a failed command and the like). */
export const useNoticesStore = defineStore('notices', () => {
  const items = ref<Notice[]>([])
  let nextId = 1

  function dismiss(id: number): void {
    items.value = items.value.filter((n) => n.id !== id)
  }

  function push(text: string, kind: Notice['kind'] = 'error'): void {
    // The same text twice in a row is one notice.
    if (items.value.some((n) => n.text === text)) return
    const id = nextId++
    items.value = [...items.value, { id, text, kind }]
    setTimeout(() => dismiss(id), LIFETIME_MS)
  }

  return { items, push, dismiss }
})
