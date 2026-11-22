import { computed, onScopeDispose, watch, type ComputedRef } from 'vue'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { useMediaQuery } from './useMediaQuery'
import { INSTALLED_APP_QUERY, isIosHomeScreenApp } from '@/utils/pwa'

function wakeLockSupported(): boolean {
  return typeof navigator !== 'undefined' && 'wakeLock' in navigator
}

/**
 * Whether "keep the screen on" is offered here: only in the installed app, where GarageStack is
 * the thing on screen (a phone in a holder, a tablet on the wall), and only where the browser can
 * hold the screen on at all. A browser tab is left to dim as usual.
 */
export function useKeepScreenOnOffered(): ComputedRef<boolean> {
  const standalone = useMediaQuery(INSTALLED_APP_QUERY)
  return computed(() => (standalone.value || isIosHomeScreenApp()) && wakeLockSupported())
}

/**
 * Holds the screen on while the setting is on and the installed app is in view. The browser lets
 * go of the lock whenever the app leaves the screen, so it is asked again each time it comes back.
 * Mounted once, in App.vue.
 */
export function useScreenWakeLock() {
  const settings = useUiSettingsStore()
  const offered = useKeepScreenOnOffered()
  const wanted = computed(() => offered.value && settings.keepScreenOn)

  let lock: WakeLockSentinel | null = null
  let requesting = false

  async function acquire() {
    if (lock || requesting || document.visibilityState !== 'visible') return
    requesting = true
    try {
      const sentinel = await navigator.wakeLock.request('screen')
      // Turned off while the browser was still answering.
      if (!wanted.value) {
        void sentinel.release()
        return
      }
      lock = sentinel
      sentinel.addEventListener('release', () => {
        if (lock === sentinel) lock = null
      })
    } catch {
      // Refused, by a battery saver for one: the screen dims as usual, and the next time the app
      // comes into view asks again.
    } finally {
      requesting = false
    }
  }

  function release() {
    const held = lock
    lock = null
    void held?.release()
  }

  function onVisibilityChange() {
    if (wanted.value && document.visibilityState === 'visible') void acquire()
  }

  watch(
    wanted,
    (on) => {
      if (on) void acquire()
      else release()
    },
    { immediate: true },
  )
  document.addEventListener('visibilitychange', onVisibilityChange)

  onScopeDispose(() => {
    document.removeEventListener('visibilitychange', onVisibilityChange)
    release()
  })
}
