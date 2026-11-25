import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import { vehicleApi, type ChargeSession, type ChargeSessionPowerPoint } from '@/services/vehicleApi'
import { useChargePower } from '../useChargePower'

vi.mock('@/services/vehicleApi', () => ({
  vehicleApi: { chargePower: vi.fn<() => Promise<ChargeSessionPowerPoint[]>>() },
}))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

const VIN = 'FAKEVN00000000001'

function session(startedAt: string, endedAt: string | null): ChargeSession {
  return {
    startedAt,
    endedAt,
    startSocPercent: 40,
    endSocPercent: 80,
    chargingType: 'AC',
    odometerKm: 10000,
    latitude: null,
    longitude: null,
    startBatteryKwh: null,
    endBatteryKwh: null,
    batteryEnergyGainedKwh: null,
    maxPowerKw: 7.2,
  }
}

const ended = session('2026-10-19T10:00:00Z', '2026-10-19T11:00:00Z')
const charging = session('2026-10-20T08:00:00Z', null)
const curve: ChargeSessionPowerPoint[] = [{ recordedAt: '2026-10-19T10:00:00Z', powerKw: 7.2 }]

function mountPower(initial: ChargeSession | null) {
  const picked = ref<ChargeSession | null>(initial)
  let power: ReturnType<typeof useChargePower> | undefined
  mount(
    defineComponent({
      setup() {
        power = useChargePower(ref(VIN), picked)
        return () => h('div')
      },
    }),
    { global: { plugins: [i18n] } },
  )
  return { picked, power: power! }
}

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(vehicleApi.chargePower).mockResolvedValue(curve)
})

describe('useChargePower', () => {
  it('asks for nothing while no session is picked', async () => {
    const { power } = mountPower(null)
    await flushPromises()

    expect(vehicleApi.chargePower).not.toHaveBeenCalled()
    expect(power.points.value).toBeNull()
  })

  it('loads a finished session once, however often it is picked', async () => {
    const { picked, power } = mountPower(ended)
    await flushPromises()
    expect(vehicleApi.chargePower).toHaveBeenCalledWith(VIN, ended.startedAt, ended.endedAt)
    expect(power.points.value).toEqual(curve)

    picked.value = charging
    await flushPromises()
    picked.value = ended
    await flushPromises()

    expect(
      vi.mocked(vehicleApi.chargePower).mock.calls.filter(([, from]) => from === ended.startedAt),
    ).toHaveLength(1)
    expect(power.points.value).toEqual(curve)
  })

  it('asks a session still charging up to now, and again each time, as its curve grows', async () => {
    const { picked } = mountPower(charging)
    await flushPromises()
    expect(vehicleApi.chargePower).toHaveBeenCalledWith(VIN, charging.startedAt, undefined)

    picked.value = ended
    await flushPromises()
    picked.value = charging
    await flushPromises()

    expect(
      vi
        .mocked(vehicleApi.chargePower)
        .mock.calls.filter(([, from]) => from === charging.startedAt),
    ).toHaveLength(2)
  })

  it('says why it failed, and loads on a retry', async () => {
    vi.mocked(vehicleApi.chargePower).mockRejectedValueOnce(new Error('offline'))
    const { power } = mountPower(ended)
    await flushPromises()
    expect(power.error.value).not.toBeNull()
    expect(power.points.value).toBeNull()

    power.retry()
    await flushPromises()

    expect(power.error.value).toBeNull()
    expect(power.points.value).toEqual(curve)
  })
})
