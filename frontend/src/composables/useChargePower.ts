import { ref, shallowRef, watch, type Ref } from 'vue'
import { vehicleApi, type ChargeSession, type ChargeSessionPowerPoint } from '@/services/vehicleApi'
import { useAsyncRequest } from './useAsyncRequest'

/**
 * The power curve of the charge session the charge history's detail shows. The sessions come
 * without it, so it is fetched one session at a time as sessions are picked. A finished session
 * picked again is not fetched again; one still charging is, as its curve has grown since.
 */
export function useChargePower(vin: Ref<string | null>, session: Ref<ChargeSession | null>) {
  const points = shallowRef<readonly ChargeSessionPowerPoint[] | null>(null)
  const { loading, error, run } = useAsyncRequest()
  const cache = new Map<string, readonly ChargeSessionPowerPoint[]>()
  const shownKey = ref<string | null>(null)

  const wanted = () =>
    vin.value && session.value
      ? {
          car: vin.value,
          startedAt: session.value.startedAt,
          endedAt: session.value.endedAt,
          key: `${vin.value}|${session.value.startedAt}|${session.value.endedAt ?? ''}`,
        }
      : null

  async function load() {
    const want = wanted()
    if (!want) {
      shownKey.value = null
      points.value = null
      return
    }
    shownKey.value = want.key
    const cached = cache.get(want.key)
    if (cached) {
      points.value = cached
      return
    }
    // The previous session's curve must not stand in for this one while it loads.
    points.value = null
    await run(
      () => vehicleApi.chargePower(want.car, want.startedAt, want.endedAt ?? undefined),
      (curve) => {
        if (want.endedAt) cache.set(want.key, curve)
        if (shownKey.value === want.key) points.value = curve
      },
    )
  }

  watch(() => wanted()?.key, load, { immediate: true })

  /** Asks again, after the curve failed to load. */
  function retry() {
    void load()
  }

  return { points, loading, error, retry }
}
