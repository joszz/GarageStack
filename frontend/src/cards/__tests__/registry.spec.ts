import { describe, it, expect } from 'vitest'
import {
  ALL_CARD_IDS,
  cardHasData,
  cardIcon,
  defaultCards,
  type CardDataContext,
  type CardId,
} from '@/cards/registry'
import type { TelemetrySnapshot, Trip } from '@/services/vehicleApi'
import type { VehicleType } from '@/stores/vehicle'

function context(
  status: Partial<TelemetrySnapshot>,
  vehicleType: VehicleType = 'phev',
  latestTrip: Trip | null = null,
): CardDataContext {
  return { status: status as TelemetrySnapshot, vehicleType, latestTrip }
}

function trip(speeds: (number | null)[]): Trip {
  return {
    points: speeds.map((speed) => ({ speed })),
  } as Trip
}

describe('card registry', () => {
  it('gives every card an icon', () => {
    const withoutIcon = ALL_CARD_IDS.filter((id) => !cardIcon(id))

    expect(withoutIcon).toEqual([])
  })

  it('treats a card without a predicate as always having data', () => {
    expect(cardHasData('odometer', context({}))).toBe(true)
  })

  it('reports no data for a value the vehicle does not send', () => {
    expect(cardHasData('fuelLevel', context({ fuelLevelPercent: null }))).toBe(false)
    expect(cardHasData('fuelLevel', context({ fuelLevelPercent: 42 }))).toBe(true)
  })

  it('hides charge-related cards on a vehicle that cannot charge externally', () => {
    const charging = { remainingChargingTime: 30, mileageSinceLastCharge: 12 }
    expect(cardHasData('remainingCharge', context(charging, 'hev'))).toBe(false)
    expect(cardHasData('remainingCharge', context(charging, 'bev'))).toBe(true)
    expect(cardHasData('chargingSession', context({}, 'hev'))).toBe(false)
    expect(cardHasData('batteryHeating', context({}, 'bev'))).toBe(true)
  })

  it('waits for the vehicle type before claiming since-charge efficiency', () => {
    const status = { mileageSinceLastCharge: 25 }
    expect(cardHasData('efficiencyCharge', context(status, 'unknown'))).toBe(false)
    expect(cardHasData('efficiencyCharge', context(status, 'hev'))).toBe(false)
    expect(cardHasData('efficiencyCharge', context(status, 'bev'))).toBe(true)
  })

  it('accepts either presentation of the efficiency ratio card', () => {
    const energy = context({ powerUsageOfDay: 8, mileageOfTheDay: 40 }, 'bev')
    const fuel = context({ fuelRangeKm: 400, fuelLevelPercent: 50 }, 'hev')
    const neither = context({ powerUsageOfDay: null, mileageOfTheDay: null }, 'bev')

    expect(cardHasData('efficiencyRatio', energy)).toBe(true)
    expect(cardHasData('efficiencyRatio', fuel)).toBe(true)
    expect(cardHasData('efficiencyRatio', neither)).toBe(false)
  })

  it('needs a trip with recorded speeds for the top speed card', () => {
    expect(cardHasData('topSpeed', context({}, 'bev', null))).toBe(false)
    expect(cardHasData('topSpeed', context({}, 'bev', trip([null, null])))).toBe(false)
    expect(cardHasData('topSpeed', context({}, 'bev', trip([null, 88])))).toBe(true)
  })
})

describe('defaultCards', () => {
  it('covers every registered card exactly once', () => {
    const ids = defaultCards('phev').map((c) => c.id)
    expect([...ids].sort()).toEqual([...ALL_CARD_IDS].sort())
  })

  it('puts visible cards before hidden ones', () => {
    const visibility = defaultCards('bev').map((c) => c.visible)
    expect(visibility).toEqual([...visibility].sort((a, b) => Number(b) - Number(a)))
  })

  it('leaves fuel cards out for a BEV and charge cards out for an HEV', () => {
    const visible = (type: VehicleType) =>
      new Set(
        defaultCards(type)
          .filter((c) => c.visible)
          .map((c) => c.id as CardId),
      )

    expect(visible('bev').has('fuelLevel')).toBe(false)
    expect(visible('bev').has('charging')).toBe(true)
    expect(visible('hev').has('charging')).toBe(false)
    expect(visible('hev').has('fuelLevel')).toBe(true)
  })
})
