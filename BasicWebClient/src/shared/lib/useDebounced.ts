import { onUnmounted, ref, watch, type Ref } from 'vue'

/** The source is a getter to work with refs and props alike: useDebounced(() => props.query). */
export function useDebounced<T>(source: () => T, delayMs = 250): Ref<T> {
  const debounced = ref(source()) as Ref<T>
  let timer: ReturnType<typeof setTimeout> | undefined

  watch(source, (value) => {
    clearTimeout(timer)
    timer = setTimeout(() => {
      debounced.value = value
    }, delayMs)
  })

  onUnmounted(() => clearTimeout(timer))

  return debounced
}
