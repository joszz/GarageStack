import { watch, ref } from 'vue'
import type { Ref } from 'vue'
import type { TelemetrySnapshot } from '@/services/vehicleApi'
import {
  useTyrePressureThresholds,
  DEFAULT_TYRE_PRESSURE_THRESHOLDS,
  type TyrePressureThresholds,
} from '@/composables/useTyrePressureThresholds'

// `t` is injected rather than obtained via useI18n() here so these stay plain, directly
// testable functions - useI18n() requires an active component setup context, which these
// don't have when called from useVehicleAlerts's own unit tests. A minimal structural type
// (rather than vue-i18n's own ComposerTranslation) avoids coupling to a specific locale's
// message-key generics, which differ between the app's real i18n instance and a test one.
type Translate = (key: string, named?: Record<string, unknown>) => string

export function getOpenItems(s: TelemetrySnapshot, t: Translate): string[] {
  const open: string[] = []
  if (s.driverDoorOpen) open.push(t('vehicle.alerts.driverDoor'))
  if (s.passengerDoorOpen) open.push(t('vehicle.alerts.passengerDoor'))
  if (s.rearLeftDoorOpen) open.push(t('vehicle.alerts.rearLeftDoor'))
  if (s.rearRightDoorOpen) open.push(t('vehicle.alerts.rearRightDoor'))
  if (s.trunkOpen) open.push(t('vehicle.alerts.boot'))
  if (s.bonnetOpen) open.push(t('vehicle.alerts.bonnet'))
  if (s.driverWindowOpen) open.push(t('vehicle.alerts.driverWindow'))
  if (s.passengerWindowOpen) open.push(t('vehicle.alerts.passengerWindow'))
  if (s.rearLeftWindowOpen) open.push(t('vehicle.alerts.rearLeftWindow'))
  if (s.rearRightWindowOpen) open.push(t('vehicle.alerts.rearRightWindow'))
  return open
}

export function getTyrePressureAlerts(
  s: TelemetrySnapshot,
  thresholds: TyrePressureThresholds = DEFAULT_TYRE_PRESSURE_THRESHOLDS,
): string[] {
  const alerts: string[] = []
  const check = (label: string, val: number | null) => {
    if (val !== null && (val < thresholds.lowBar || val > thresholds.highBar)) {
      alerts.push(`${label}: ${val.toFixed(2)} bar`)
    }
  }
  check('FL', s.tyrePressureFrontLeft)
  check('FR', s.tyrePressureFrontRight)
  check('RL', s.tyrePressureRearLeft)
  check('RR', s.tyrePressureRearRight)
  return alerts
}

function showNotification(title: string, body: string) {
  if (!('Notification' in window) || Notification.permission !== 'granted') return
  try {
    new Notification(title, { body, icon: '/pwa-192x192.png' })
  } catch {
    // may fail outside a service worker context on some platforms
  }
}

// Fires `notify` once when `issues` goes from empty to non-empty, then stays quiet until
// `issues` is empty again, at which point it resets and can fire again on the next issue.
function useStickyAlert<T>(notify: (issues: T[]) => void) {
  const sent = ref(false)
  return (issues: T[]) => {
    if (issues.length > 0 && !sent.value) {
      sent.value = true
      notify(issues)
    } else if (issues.length === 0) {
      sent.value = false
    }
  }
}

export function useVehicleAlerts(status: Ref<TelemetrySnapshot | null>, t: Translate) {
  const tyreThresholds = useTyrePressureThresholds()
  const checkOpenAlert = useStickyAlert<string>((open) =>
    showNotification(
      'GarageStack',
      t('vehicle.alerts.openWhileParked', { items: open.join(', ') }),
    ),
  )
  const checkTyreAlert = useStickyAlert<string>((tyreIssues) =>
    showNotification(
      'GarageStack',
      t('vehicle.alerts.tyrePressure', { items: tyreIssues.join(', ') }),
    ),
  )

  watch(status, (s) => {
    if (!s) return

    if (s.engineRunning === false) {
      checkOpenAlert(getOpenItems(s, t))
    } else if (s.engineRunning === true) {
      checkOpenAlert([])
    }

    checkTyreAlert(getTyrePressureAlerts(s, tyreThresholds.value))
  })
}
