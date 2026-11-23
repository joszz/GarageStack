import { computed, ref, watch, type Ref, type WritableComputedRef } from 'vue'
import { demoApi, type DemoStatusOverride } from '@/services/demoApi'
import type { TelemetrySnapshot } from '@/services/vehicleApi'
import { useVehicleStore } from '@/stores/vehicle'
import { useGatewayStatus } from '@/composables/useGatewayStatus'
import { chargingKind } from '@/utils/chargeSessions'
import { mayBurnFuel } from '@/utils/vehicleType'

/** The car's on/off fields a demo switch flips as they are. */
export type DemoSwitchField = keyof DemoStatusOverride &
  keyof TelemetrySnapshot &
  (
    | 'isLocked'
    | 'engineRunning'
    | 'climateOn'
    | 'driverDoorOpen'
    | 'passengerDoorOpen'
    | 'rearLeftDoorOpen'
    | 'rearRightDoorOpen'
    | 'trunkOpen'
    | 'bonnetOpen'
    | 'driverWindowOpen'
    | 'passengerWindowOpen'
    | 'rearLeftWindowOpen'
    | 'rearRightWindowOpen'
    | 'chargerConnected'
    | 'isCharging'
    | 'isAvailable'
  )

/** The car's numbers a demo slider sets as they are, in the metric unit the car reports. */
export type DemoSliderField = keyof DemoStatusOverride &
  keyof TelemetrySnapshot &
  (
    | 'evSocPercent'
    | 'fuelLevelPercent'
    | 'speed'
    | 'interiorTemperature'
    | 'exteriorTemperature'
    | 'tyrePressureFrontLeft'
    | 'tyrePressureFrontRight'
    | 'tyrePressureRearLeft'
    | 'tyrePressureRearRight'
    | 'batteryVoltage'
  )

export type DemoLights = 'off' | 'side' | 'dipped' | 'main'
export type DemoMotion = 'parked' | 'driving'
export type DemoChargingType = 'AC' | 'DC'

/** A slider's value while it is moved, and sending it once it is let go. */
export interface DemoSlider {
  value: Ref<number>
  commit: () => void
}

/**
 * What the Demo Controls read from and change on the car shown: its state as the dashboard has it,
 * and every change sent for that car alone, after which the dashboard fetches the car again. A
 * change that fails leaves the car as it was and says so in `failed`.
 */
export function useDemoControls() {
  const vehicleStore = useVehicleStore()
  const gateway = useGatewayStatus()
  const status = computed(() => vehicleStore.currentStatus)
  const failed = ref(false)

  async function send(change: DemoStatusOverride) {
    const vin = vehicleStore.activeVin
    if (!vin) return
    failed.value = false
    try {
      await demoApi.setStatus(vin, change)
      await vehicleStore.fetchStatus(vin)
    } catch {
      failed.value = true
    }
  }

  /** A switch showing one of the car's on/off fields, and flipping it. Null is not known yet. */
  function switchFor(field: DemoSwitchField): WritableComputedRef<boolean | null> {
    return computed({
      get: () => status.value?.[field] ?? null,
      set: (on) => void send({ [field]: on === true }),
    })
  }

  /**
   * A slider on one of the car's numbers. It follows the car until it is moved, and sends where it
   * was let go, so dragging sends one change, not one per step.
   */
  function sliderFor(field: DemoSliderField, fallback: number): DemoSlider {
    const value = ref(status.value?.[field] ?? fallback)
    watch(
      () => status.value?.[field],
      (reported) => {
        if (reported != null) value.value = reported
      },
    )
    return { value, commit: () => void send({ [field]: value.value }) }
  }

  // The light switch has one position at a time; each lights the lamps below it as well.
  const lights = computed<DemoLights>({
    get: () => {
      if (status.value?.lightsMainBeam) return 'main'
      if (status.value?.lightsDippedBeam) return 'dipped'
      if (status.value?.lightsSide) return 'side'
      return 'off'
    },
    set: (mode) =>
      void send({
        lightsMainBeam: mode === 'main',
        lightsDippedBeam: mode === 'dipped',
        lightsSide: mode !== 'off',
      }),
  })

  // On the road while it moves or has a journey under way.
  const motion = computed<DemoMotion>({
    get: () =>
      (status.value?.speed ?? 0) > 0 || (status.value?.currentJourneyDistance ?? 0) > 0
        ? 'driving'
        : 'parked',
    set: (value) => void send({ driving: value === 'driving' }),
  })

  const chargingType = computed<DemoChargingType>({
    get: () => chargingKind(status.value?.chargingType ?? null) ?? 'AC',
    set: (type) => void send({ chargingType: type }),
  })

  // What the charger gives: the onboard charger's draw on AC, the battery's intake on DC.
  const chargingPower: DemoSlider = (() => {
    const reported = () => {
      const s = status.value
      if (!s?.isCharging) return null
      return chargingType.value === 'DC' ? Math.abs(s.hvPower ?? 0) : s.obcPowerSinglePhase
    }
    const value = ref(reported() || 7)
    watch(reported, (kw) => {
      if (kw) value.value = kw
    })
    return { value, commit: () => void send({ chargingPowerKw: value.value }) }
  })()

  // The demo gateway is signed in until the controls say otherwise.
  const gatewaySignedIn = computed<boolean | null>({
    get: () => gateway.signedIn.value !== false,
    set: (on) => {
      failed.value = false
      demoApi
        .setGateway(on === true)
        .then(() => gateway.refresh())
        .catch(() => {
          failed.value = true
        })
    },
  })

  // A fuel tank only on a car that may have one: a hybrid, or one whose drivetrain is not known.
  const hasFuel = computed(() => mayBurnFuel(vehicleStore.effectiveVehicleType))

  const carLabel = computed(() => vehicleStore.carLabelFor(vehicleStore.activeVehicle?.id))

  return {
    status,
    failed,
    hasFuel,
    carLabel,
    switchFor,
    sliderFor,
    lights,
    motion,
    chargingType,
    chargingPower,
    gatewaySignedIn,
  }
}
