import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { UnitFormatter } from '@/utils/units'

/**
 * The formatter for the units this browser shows: the chosen ones, and the region's for each one
 * left on automatic. The store resolves them into a fresh object whenever any of them changes, so
 * anything built on this computed follows. The language needs no such care: each symbol is
 * translated when it is asked for, inside whatever computed or render is asking.
 */
export function useUnits() {
  const settings = useUiSettingsStore()
  const { t } = useI18n()
  return computed(() => new UnitFormatter(settings.effectiveUnits, t))
}
