import { watch, ref } from 'vue'
import type { Ref } from 'vue'
import type { TelemetrySnapshot } from '@/services/vehicleApi'
import {
  useTyrePressureThresholds,
  DEFAULT_TYRE_PRESSURE_THRESHOLDS,
  type TyrePressureThresholds,
} from '@/composables/useTyrePressureThresholds'

export function getOpenItems(s: TelemetrySnapshot): string[] {
  const open: string[] = []
  if (s.driverDoorOpen) open.push('driver door')
  if (s.passengerDoorOpen) open.push('passenger door')
  if (s.rearLeftDoorOpen) open.push('rear left door')
  if (s.rearRightDoorOpen) open.push('rear right door')
  if (s.trunkOpen) open.push('boot')
  if (s.bonnetOpen) open.push('bonnet')
  if (s.driverWindowOpen) open.push('driver window')
  if (s.passengerWindowOpen) open.push('passenger window')
  if (s.rearLeftWindowOpen) open.push('rear left window')
  if (s.rearRightWindowOpen) open.push('rear right window')
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

export function useVehicleAlerts(status: Ref<TelemetrySnapshot | null>) {
  const tyreThresholds = useTyrePressureThresholds()
  const checkOpenAlert = useStickyAlert<string>((open) =>
    showNotification('GarageStack', `Open while parked: ${open.join(', ')}`),
  )
  const checkTyreAlert = useStickyAlert<string>((tyreIssues) =>
    showNotification('GarageStack', `Tyre pressure: ${tyreIssues.join(', ')}`),
  )

  watch(status, (s) => {
    if (!s) return

    if (s.engineRunning === false) {
      checkOpenAlert(getOpenItems(s))
    } else if (s.engineRunning === true) {
      checkOpenAlert([])
    }

    checkTyreAlert(getTyrePressureAlerts(s, tyreThresholds.value))
  })
}
