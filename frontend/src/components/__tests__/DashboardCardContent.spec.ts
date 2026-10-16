import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import type { TelemetrySnapshot } from '@/services/vehicleApi'
import type { CardId } from '@/cards/registry'
import { useVehicleStore } from '@/stores/vehicle'
import { useUiSettingsStore } from '@/stores/settingsUi'
import type { VehicleType } from '@/stores/vehicle'
import { METRIC_UNITS } from '@/utils/units'
import DashboardCardContent from '../DashboardCardContent.vue'

vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn<(to: unknown) => void>() }) }))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

// Renders what the plain status card is given, so a spec can read it back.
const StatusCardStub = {
  name: 'StatusCard',
  props: ['icon', 'label', 'value', 'unit', 'variant'],
  template:
    '<div class="status-card-stub" :data-variant="variant" :data-icon="icon">{{ label }}|{{ value }}|{{ unit }}</div>',
}

// The fields the simple cards read, all empty unless a spec fills them in.
function status(values: Partial<TelemetrySnapshot> = {}): TelemetrySnapshot {
  return {
    fuelLevelPercent: null,
    fuelRangeKm: null,
    evSocPercent: null,
    electricRangeKm: null,
    isCharging: null,
    odometerKm: null,
    batteryVoltage: null,
    sunRoofOpen: null,
    mileageOfTheDay: null,
    powerUsageOfDay: null,
    mileageSinceLastCharge: null,
    speed: null,
    remainingChargingTime: null,
    currentJourneyDistance: null,
    ...values,
  } as TelemetrySnapshot
}

function render(cardId: CardId, snapshot: TelemetrySnapshot | null, type: VehicleType = 'bev') {
  useUiSettingsStore().vehicleTypeOverride = type
  useVehicleStore().currentStatus = snapshot
  const wrapper = mount(DashboardCardContent, {
    props: { cardId },
    global: { plugins: [i18n], stubs: { StatusCard: StatusCardStub, FontAwesomeIcon: true } },
  })
  const card = wrapper.find('.status-card-stub')
  return {
    wrapper,
    text: card.exists() ? card.text() : null,
    variant: card.exists() ? card.attributes('data-variant') : undefined,
    icon: card.exists() ? card.attributes('data-icon') : undefined,
  }
}

describe('DashboardCardContent', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useUiSettingsStore().units = { ...METRIC_UNITS }
  })

  it('renders nothing before the first status arrives', () => {
    expect(render('fuelLevel', null).wrapper.html()).toBe('<!--v-if-->')
  })

  it('shows the fuel level as a whole percentage', () => {
    expect(render('fuelLevel', status({ fuelLevelPercent: 47.6 }), 'hev').text).toBe(
      `${en.vehicle.fuel}|48|%`,
    )
  })

  it('warns about a 12V battery below 12 V', () => {
    expect(render('battery12v', status({ batteryVoltage: 11.8 })).variant).toBe('danger')
    expect(render('battery12v', status({ batteryVoltage: 12.6 })).variant).toBe('success')
  })

  it('shows the odometer in whole kilometres', () => {
    expect(render('odometer', status({ odometerKm: 12345.6 })).text).toBe(
      `${en.vehicle.odometer}|12,346|km`,
    )
  })

  describe('efficiency', () => {
    it('is energy per kilometre on a plug-in car', () => {
      const card = render(
        'efficiencyRatio',
        status({ powerUsageOfDay: 3, mileageOfTheDay: 20 }),
        'bev',
      )

      expect(card.text).toBe(`${en.vehicle.efficiency.efficiency}|150|Wh/km`)
    })

    // A plain hybrid's energy counter holds fuel (1/100 L), so the same fields read as L/100 km.
    it('is measured fuel consumption on a hybrid', () => {
      const card = render(
        'efficiencyRatio',
        status({ powerUsageOfDay: 120, mileageOfTheDay: 20 }),
        'hev',
      )

      expect(card.text).toBe(`${en.vehicle.efficiency.consumption}|6.0|L/100 km`)
    })

    it('falls back to the range computer on a hybrid that has not driven today', () => {
      const card = render(
        'efficiencyRatio',
        status({ fuelRangeKm: 500, fuelLevelPercent: 50 }),
        'hev',
      )

      expect(card.text).toContain(en.vehicle.efficiency.fuelEconomy)
      expect(card.icon).toBe('gas-pump')
    })
  })

  it('names the energy used today in litres of fuel on a hybrid', () => {
    const card = render('efficiencyEnergy', status({ powerUsageOfDay: 150 }), 'hev')

    expect(card.text).toBe(`${en.vehicle.efficiency.todayFuel}|1.5|L`)
    expect(card.icon).toBe('gas-pump')
  })
})
