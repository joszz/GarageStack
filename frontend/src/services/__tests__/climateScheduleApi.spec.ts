import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { climateScheduleApi } from '@/services/climateScheduleApi'
import { toRequest } from '@/stores/climateSchedules'
import { makeResponse, type FetchSpy } from './fetchStub'
import { schedule } from './climateScheduleFixtures'

describe('climateScheduleApi', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('list() reads the vehicle-scoped schedules', async () => {
    const fetchSpy = vi.fn<FetchSpy>().mockResolvedValue(makeResponse(200, [schedule()]))
    vi.stubGlobal('fetch', fetchSpy)

    expect(await climateScheduleApi.list('VIN1')).toEqual([schedule()])
    expect(fetchSpy.mock.calls[0]![0]).toBe('/api/vehicles/VIN1/climate-schedules')
  })

  it('create() posts the settings and returns the saved schedule', async () => {
    const fetchSpy = vi.fn<FetchSpy>().mockResolvedValue(makeResponse(200, schedule()))
    vi.stubGlobal('fetch', fetchSpy)

    const result = await climateScheduleApi.create('VIN1', toRequest(schedule()))

    const [url, options] = fetchSpy.mock.calls[0]!
    expect(url).toBe('/api/vehicles/VIN1/climate-schedules')
    expect(options!.method).toBe('POST')
    expect(JSON.parse(options!.body as string)).toEqual(toRequest(schedule()))
    expect(result).toEqual(schedule())
  })

  it('update() puts to the schedule and delete() deletes it', async () => {
    const fetchSpy = vi.fn<FetchSpy>().mockResolvedValue(makeResponse(200, schedule()))
    vi.stubGlobal('fetch', fetchSpy)

    await climateScheduleApi.update('VIN1', 7, toRequest(schedule()))
    await climateScheduleApi.delete('VIN1', 7)

    expect(fetchSpy.mock.calls.map(([url, options]) => [url, options!.method])).toEqual([
      ['/api/vehicles/VIN1/climate-schedules/7', 'PUT'],
      ['/api/vehicles/VIN1/climate-schedules/7', 'DELETE'],
    ])
  })
})
