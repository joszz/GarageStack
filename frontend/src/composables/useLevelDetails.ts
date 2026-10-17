import { computed, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { TelemetrySnapshot } from '@/services/vehicleApi'
import { useVehicleStore } from '@/stores/vehicle'
import type { LevelKind } from '@/composables/useStatisticsCharts'
import { formatDateTime, formatNumber } from '@/utils/format'
import type { BadgeVariant } from '@/utils/levels'
import { plugsIn } from '@/utils/vehicleType'
import { useUnits } from './useUnits'

/** One line of what the car reports about a level now; `badge` shows the value as a status. */
export interface LevelDetail {
  key: string
  icon: string
  label: string
  value: string
  badge?: BadgeVariant
}

type Candidate = Omit<LevelDetail, 'value'> & { value: string | null }

function percent(value: number | null): string | null {
  return value === null ? null : `${Math.round(value)}%`
}

/**
 * What the live status says about the tank or the battery, for the level dialog: the level and
 * the range it is good for, and on a car that plugs in, where its charging stands. A line the
 * car reports nothing for is left out.
 */
export function useLevelDetails(level: Ref<LevelKind>): Ref<LevelDetail[]> {
  const { t } = useI18n()
  const store = useVehicleStore()
  const units = useUnits()

  function distance(km: number | null): string | null {
    return units.value.format('distance', km, { decimals: 0 })
  }

  function fuel(s: TelemetrySnapshot): Candidate[] {
    return [
      {
        key: 'level',
        icon: 'gas-pump',
        label: t('vehicle.fuel'),
        value: percent(s.fuelLevelPercent),
      },
      { key: 'range', icon: 'road', label: t('vehicle.range'), value: distance(s.fuelRangeKm) },
    ]
  }

  // The charger's state reads as one status: charging beats merely plugged in.
  function charger(s: TelemetrySnapshot): Candidate {
    const connected = s.isCharging || s.chargerConnected
    const known = s.isCharging !== null || s.chargerConnected !== null
    return {
      key: 'charger',
      icon: 'plug',
      label: t('vehicle.hvBattery.charger'),
      value: !known
        ? null
        : s.isCharging
          ? t('vehicle.chargingYes')
          : connected
            ? t('vehicle.hvBattery.pluggedIn')
            : t('vehicle.hvBattery.unplugged'),
      badge: connected ? 'info' : 'secondary',
    }
  }

  function charging(s: TelemetrySnapshot): Candidate[] {
    return [
      charger(s),
      {
        key: 'remaining',
        icon: 'clock',
        label: t('vehicle.remainingCharge'),
        value:
          s.isCharging && s.remainingChargingTime !== null
            ? `${s.remainingChargingTime} ${t('common.min')}`
            : null,
      },
      {
        key: 'target',
        icon: 'battery-full',
        label: t('vehicle.hvBattery.chargeTarget'),
        value: percent(s.socTargetPercent),
      },
      {
        key: 'lastEnd',
        icon: 'calendar-check',
        label: t('vehicle.chargingSession.lastEnd'),
        value: s.chargingLastEndAt ? formatDateTime(s.chargingLastEndAt) : null,
      },
      {
        key: 'lastEndSoc',
        icon: 'percent',
        label: t('vehicle.chargingSession.lastEndSoc'),
        value:
          s.lastChargeEndingPower === null ? null : `${formatNumber(s.lastChargeEndingPower, 0)}%`,
      },
    ]
  }

  function battery(s: TelemetrySnapshot): Candidate[] {
    return [
      { key: 'level', icon: 'bolt', label: t('vehicle.evSoc'), value: percent(s.evSocPercent) },
      {
        key: 'range',
        icon: 'road',
        label: t('vehicle.electricRange'),
        value: distance(s.electricRangeKm),
      },
      ...(plugsIn(store.effectiveVehicleType) ? charging(s) : []),
    ]
  }

  return computed(() => {
    const s = store.currentStatus
    if (!s) return []
    const candidates = level.value === 'fuel' ? fuel(s) : battery(s)
    return candidates.filter((c): c is LevelDetail => c.value !== null)
  })
}
