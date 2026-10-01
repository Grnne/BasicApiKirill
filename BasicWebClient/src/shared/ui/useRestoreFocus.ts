import { onMounted, onUnmounted } from 'vue'

/**
 * A dialog gives the keyboard back where it was: closed, it would otherwise leave the focus on
 * the page's body, and the user would start over from the top.
 */
export function useRestoreFocus(): void {
  let before: HTMLElement | null = null
  onMounted(() => {
    before = document.activeElement instanceof HTMLElement ? document.activeElement : null
  })
  onUnmounted(() => {
    if (before?.isConnected) before.focus()
  })
}
