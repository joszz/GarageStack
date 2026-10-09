import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { toRequest, useClimateSchedulesStore } from '@/stores/climateSchedules'
import { climateScheduleApi, type ClimateSchedule } from '@/services/climateScheduleApi'
import { schedule } from '@/services/__tests__/climateScheduleFixtures'

vi.mock('@/services/climateScheduleApi', () => ({
  climateScheduleApi: {
    list: vi.fn<() => Promise<ClimateSchedule[]>>().mockResolvedValue([]),
    create: vi.fn<() => Promise<ClimateSchedule>>(),
    update: vi.fn<() => Promise<ClimateSchedule>>(),
    delete: vi.fn<() => Promise<void>>().mockResolvedValue(undefined),
  },
}))

describe('useClimateSchedulesStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('keeps the list in order of time of day as schedules are added', async () => {
    const store = useClimateSchedulesStore()
    store.schedules = [schedule({ id: 1, startTime: '08:00' })]
    vi.mocked(climateScheduleApi.create).mockResolvedValue(schedule({ id: 2, startTime: '06:45' }))

    await store.createSchedule('VIN1', toRequest(schedule({ startTime: '06:45' })))

    expect(store.schedules.map((s) => s.id)).toEqual([2, 1])
  })

  it('switches a schedule on or off by saving all its settings', async () => {
    const store = useClimateSchedulesStore()
    const work = schedule({ id: 3, name: 'Work', seatLeftLevel: 2 })
    store.schedules = [work]
    vi.mocked(climateScheduleApi.update).mockResolvedValue({ ...work, enabled: false })

    await store.setEnabled('VIN1', work, false)

    expect(climateScheduleApi.update).toHaveBeenCalledWith('VIN1', 3, {
      ...toRequest(work),
      enabled: false,
    })
    expect(store.schedules[0]!.enabled).toBe(false)
  })

  it('names the switched-on schedule that runs first as the next one', () => {
    const store = useClimateSchedulesStore()
    store.schedules = [
      schedule({ id: 1, nextRunUtc: '2026-10-12T05:30:00Z' }),
      schedule({ id: 2, nextRunUtc: '2026-10-10T06:15:00Z' }),
      schedule({ id: 3, enabled: false, nextRunUtc: null }),
    ]

    expect(store.nextSchedule?.id).toBe(2)
    expect(store.activeSchedules).toHaveLength(2)
  })

  it('removes a deleted schedule and keeps the error of a failed save', async () => {
    const store = useClimateSchedulesStore()
    store.schedules = [schedule({ id: 1 }), schedule({ id: 2 })]

    await store.deleteSchedule('VIN1', 1)
    expect(store.schedules.map((s) => s.id)).toEqual([2])

    vi.mocked(climateScheduleApi.update).mockRejectedValue(new Error('nope'))
    await store.updateSchedule('VIN1', 2, toRequest(schedule()))
    expect(store.actionError?.message).toBe('nope')
  })
})
