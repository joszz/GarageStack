import { onUnmounted, readonly, ref, type Ref } from 'vue'

/**
 * Whether the viewport matches `query`, kept up to date as the window is resized or a tablet is
 * turned. For the few layout choices that change what is mounted rather than how it looks, such
 * as a panel beside a list or a modal over it; everything else belongs in a stylesheet.
 * False where matchMedia is missing.
 */
export function useMediaQuery(query: string): Readonly<Ref<boolean>> {
  const list = typeof window !== 'undefined' ? window.matchMedia?.(query) : undefined
  const matches = ref(list?.matches === true)

  const onChange = (event: MediaQueryListEvent) => {
    matches.value = event.matches
  }
  list?.addEventListener('change', onChange)
  onUnmounted(() => list?.removeEventListener('change', onChange))

  return readonly(matches)
}
