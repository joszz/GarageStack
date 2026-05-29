import { watch, ref } from 'vue'
import type { Ref } from 'vue'
import type { TelemetrySnapshot } from '@/services/api'

const TYRE_LOW = 1.8
const TYRE_HIGH = 3.2

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
  if (s.sunRoofOpen) open.push('sunroof')
  return open
}

export function getTyrePressureAlerts(s: TelemetrySnapshot): string[] {
  const alerts: string[] = []
  const check = (label: string, val: number | null) => {
    if (val !== null && (val < TYRE_LOW || val > TYRE_HIGH)) {
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

export function useVehicleAlerts(status: Ref<TelemetrySnapshot | null>) {
  const openAlertSent = ref(false)
  const tyreAlertSent = ref(false)

  watch(status, (s) => {
    if (!s) return

    if (!s.engineRunning) {
      const open = getOpenItems(s)
      if (open.length > 0 && !openAlertSent.value) {
        openAlertSent.value = true
        showNotification('GarageStack', `Open while parked: ${open.join(', ')}`)
      } else if (open.length === 0) {
        openAlertSent.value = false
      }
    } else {
      openAlertSent.value = false
    }

    const tyreIssues = getTyrePressureAlerts(s)
    if (tyreIssues.length > 0 && !tyreAlertSent.value) {
      tyreAlertSent.value = true
      showNotification('GarageStack', `Tyre pressure: ${tyreIssues.join(', ')}`)
    } else if (tyreIssues.length === 0) {
      tyreAlertSent.value = false
    }
  })
}
