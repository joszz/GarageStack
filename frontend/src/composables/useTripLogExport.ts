import { onUnmounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { tripLogApi, type TripLogEntry } from '@/services/tripLogApi'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { useVehicleStore } from '@/stores/vehicle'
import { useUnits } from '@/composables/useUnits'
import { downloadCsv } from '@/utils/download'
import { asError } from '@/utils/errors'
import { missingPlaces, periodRange, type TripLogPeriod } from '@/utils/tripLog'
import { tripLogCsv, tripLogFileName } from '@/utils/tripLogCsv'
import { lookUpTripPlaces, withPlaces } from '@/utils/tripLogPlaces'
import { localeLanguage } from '@/utils/locale'

/**
 * Saves a period of the trip log as CSV, hidden trips left out. It reads the trips itself rather
 * than through the trip log store, so exporting never changes what the trip log page shows. With
 * place names on, the addresses nobody has looked up yet are looked up first, so the file has a
 * place where it can; an end with nothing found stays as coordinates. Leaving the page that asked
 * (closing the settings) stops the lookup and saves nothing.
 */
export function useTripLogExport() {
  const { t } = useI18n()
  const units = useUnits()
  const uiSettings = useUiSettingsStore()
  const vehicleStore = useVehicleStore()

  const busy = ref(false)
  /** How many trips still wait for an address while they are looked up; null otherwise. */
  const remaining = ref<number | null>(null)
  /** True when the last period asked for had no trips to export. */
  const empty = ref(false)
  const error = ref<Error | null>(null)
  let stopped = false
  onUnmounted(() => {
    stopped = true
  })

  async function addPlaces(vin: string, trips: TripLogEntry[]) {
    let entries = trips
    const count = () => (remaining.value = entries.filter(missingPlaces).length)
    count()
    let available = true
    try {
      available = await lookUpTripPlaces(vin, localeLanguage(uiSettings.locale), {
        entries: () => entries,
        apply: (found) => {
          entries = withPlaces(entries, found)
          count()
        },
        active: () => !stopped,
        onEnd: () => {
          remaining.value = null
        },
      })
    } catch {
      // Offline part way: what was found so far goes in, and the rest as coordinates.
    }
    return { entries, available }
  }

  async function download(vin: string, period: TripLogPeriod) {
    if (busy.value) return
    busy.value = true
    empty.value = false
    error.value = null
    try {
      const { from, to } = periodRange(period)
      const loaded = await tripLogApi.log(vin, from.toISOString(), to.toISOString())
      let entries = loaded.filter((entry) => !entry.isHidden)
      if (entries.length === 0) {
        empty.value = true
        return
      }

      let placesShown = uiSettings.placeNamesEnabled
      if (placesShown) {
        const result = await addPlaces(vin, entries)
        entries = result.entries
        placesShown = result.available
      }
      if (stopped) return

      const csv = tripLogCsv(entries, {
        t,
        units: units.value,
        placesShown,
        vehicleType: vehicleStore.effectiveVehicleType,
      })
      downloadCsv(tripLogFileName(period), csv)
    } catch (e) {
      error.value = asError(e)
    } finally {
      busy.value = false
    }
  }

  return { busy, remaining, empty, error, download }
}
