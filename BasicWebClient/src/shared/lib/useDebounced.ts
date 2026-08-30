import { onUnmounted, ref, watch, type Ref } from 'vue'

/**
 * Возвращает копию значения, которая обновляется не сразу, а после паузы.
 * Нужна для поиска: без неё запрос уходит на каждую букву.
 *
 * Источник передаётся функцией — так работает и с ref, и с props:
 *   const debounced = useDebounced(() => props.query)
 */
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
