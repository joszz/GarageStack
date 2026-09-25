import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { UnitFormatter } from '@/utils/units'

/**
 * The formatter for this browser's units. The preferences are copied in, rather than handed over
 * as the store's reactive object, so this computed depends on every one of them and anything
 * built on it follows a change. The language needs no such care: each symbol is translated when
 * it is asked for, inside whatever computed or render is asking.
 */
export function useUnits() {
  const settings = useUiSettingsStore()
  const { t } = useI18n()
  return computed(() => new UnitFormatter({ ...settings.units }, t))
}
