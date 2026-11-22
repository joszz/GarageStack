import { computed, onScopeDispose, readonly, ref, watch, type ComputedRef } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { useVehicleStore } from '@/stores/vehicle'
import { ApiError } from '@/services/apiCore'
import { MAX_BATCH, phonePositionsApi } from '@/services/phonePositionsApi'
import {
  dropQueued,
  phoneTrackerId,
  queuedPositions,
  queuePosition,
  toPhonePosition,
  worthKeeping,
  type KeptFix,
} from '@/utils/phoneGps'
import { useKeepScreenOnOffered } from './useScreenWakeLock'

/** How often the queue goes out while recording: well inside the Worker's wait after a trip. */
export const FLUSH_INTERVAL_MS = 30_000
/** A queue this long goes out at once, rather than waiting for the next round. */
export const FLUSH_AT = 100

// Shared with the settings, which say when the browser refused and when it records.
const denied = ref(false)
const recording = ref(false)

/** What the recorder is doing, for the settings to show. */
export function usePhoneGpsStatus() {
  return { denied: readonly(denied), recording: readonly(recording) }
}

/**
 * Whether recording GPS is offered here: where the screen can be kept on (the installed app) and
 * the browser has a location to give. A web page only gets one while it is on screen.
 */
export function usePhoneGpsOffered(): ComputedRef<boolean> {
  const screen = useKeepScreenOnOffered()
  return computed(
    () => screen.value && typeof navigator !== 'undefined' && 'geolocation' in navigator,
  )
}

/**
 * Records the phone's position for the selected car while the installed app is on screen with
 * "keep the screen on" and "record GPS" both on, and sends it to the API, which adds it to the
 * car's trips. Positions wait in this browser's storage until they are sent, so a stretch without
 * signal loses nothing. Mounted once, in App.vue.
 */
export function usePhoneGpsRecorder() {
  const settings = useUiSettingsStore()
  const auth = useAuthStore()
  const vehicles = useVehicleStore()
  const offered = usePhoneGpsOffered()
  const visible = ref(document.visibilityState === 'visible')

  /** The car being recorded for, or null while nothing is. */
  const target = computed(() =>
    offered.value &&
    settings.keepScreenOn &&
    settings.recordPhoneGps &&
    auth.isAuthenticated &&
    visible.value
      ? vehicles.activeVin
      : null,
  )

  // The watch, with the geolocation it belongs to, so stopping never depends on finding it again.
  let watching: { geolocation: Geolocation; id: number } | null = null
  let flushTimer: ReturnType<typeof setInterval> | null = null
  let last: KeptFix | null = null
  let flushing = false

  function onPosition(vin: string, position: GeolocationPosition) {
    denied.value = false
    const fix = {
      time: position.timestamp,
      latitude: position.coords.latitude,
      longitude: position.coords.longitude,
    }
    if (!worthKeeping(last, fix, position.coords.accuracy)) return
    last = fix
    queuePosition(vin, toPhonePosition(position))
    if (queuedPositions(vin).length >= FLUSH_AT) void flush(vin)
  }

  function onError(error: GeolocationPositionError) {
    // Refused: nothing comes until the setting is switched on again. A timeout or no fix (a
    // tunnel, a car park) passes, and the watch carries on.
    if (error.code !== error.PERMISSION_DENIED) return
    denied.value = true
    stop()
  }

  function start(vin: string) {
    stop()
    last = null
    const geolocation = navigator.geolocation
    const id = geolocation.watchPosition((p) => onPosition(vin, p), onError, {
      enableHighAccuracy: true,
      maximumAge: 0,
      timeout: 60_000,
    })
    watching = { geolocation, id }
    recording.value = true
    flushTimer = setInterval(() => void flush(vin), FLUSH_INTERVAL_MS)
    // What an earlier visit could not send.
    void flush(vin)
  }

  function stop() {
    watching?.geolocation.clearWatch(watching.id)
    watching = null
    if (flushTimer !== null) clearInterval(flushTimer)
    flushTimer = null
    recording.value = false
  }

  async function flush(vin: string) {
    if (flushing) return
    flushing = true
    try {
      const trackerId = phoneTrackerId()
      for (;;) {
        const batch = queuedPositions(vin).slice(0, MAX_BATCH)
        if (batch.length === 0) return
        try {
          await phonePositionsApi.upload(vin, trackerId, batch)
        } catch (error) {
          // A batch the API refuses (malformed, or a car it no longer has) would be refused
          // again, so it goes. No signal, the server away or too many requests: the next round.
          if (!(error instanceof ApiError) || (error.status !== 400 && error.status !== 404)) return
        }
        dropQueued(vin, batch)
      }
    } finally {
      flushing = false
    }
  }

  function onVisibilityChange() {
    visible.value = document.visibilityState === 'visible'
  }
  document.addEventListener('visibilitychange', onVisibilityChange)

  // Switched on again after the browser refused: ask once more.
  watch(
    () => settings.recordPhoneGps,
    (on) => {
      if (on) denied.value = false
    },
  )

  watch(
    target,
    (vin, before) => {
      // Whatever the last car's recording left gets one more try on the way out.
      if (before) void flush(before)
      if (vin && !denied.value) start(vin)
      else stop()
    },
    { immediate: true },
  )

  onScopeDispose(() => {
    document.removeEventListener('visibilitychange', onVisibilityChange)
    stop()
  })
}
