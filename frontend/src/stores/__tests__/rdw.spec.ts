import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useRdwStore } from '@/stores/rdw'
import { ApiError } from '@/services/apiCore'
import type { RdwStatus } from '@/services/rdwApi'

vi.mock('@/services/rdwApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/services/rdwApi')>()),
  rdwApi: {
    get: vi.fn<() => Promise<RdwStatus>>(),
    setLicensePlate: vi.fn<() => Promise<RdwStatus>>(),
  },
}))

const withPlate: RdwStatus = { licensePlate: 'AB123C', info: null }

describe('useRdwStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('learns that the deployment looks plates up from the first answer', async () => {
    const { rdwApi } = await import('@/services/rdwApi')
    vi.mocked(rdwApi.get).mockResolvedValue(withPlate)
    const store = useRdwStore()

    await store.fetchStatus('VIN1')

    expect(store.available).toBe(true)
    expect(store.hasPlate).toBe(true)
  })

  it('treats rdw.disabled as switched off, and does not ask again', async () => {
    const { rdwApi } = await import('@/services/rdwApi')
    vi.mocked(rdwApi.get).mockRejectedValue(
      new ApiError(404, '/api/vehicles/VIN1/rdw', { code: 'rdw.disabled' }),
    )
    const store = useRdwStore()

    await store.fetchStatus('VIN1')
    await store.fetchStatus('VIN1')

    expect(store.available).toBe(false)
    expect(store.hasPlate).toBe(false)
    expect(store.error).toBeNull()
    expect(rdwApi.get).toHaveBeenCalledTimes(1)
  })

  it('keeps any other failure as an error, without deciding the feature is off', async () => {
    const { rdwApi } = await import('@/services/rdwApi')
    vi.mocked(rdwApi.get).mockRejectedValue(new ApiError(500, '/api/vehicles/VIN1/rdw'))
    const store = useRdwStore()

    await store.fetchStatus('VIN1')

    expect(store.available).toBeNull()
    expect(store.error).not.toBeNull()
  })

  it('has no plate to show while the feature is on but no plate is set', async () => {
    const { rdwApi } = await import('@/services/rdwApi')
    vi.mocked(rdwApi.get).mockResolvedValue({ licensePlate: null, info: null })
    const store = useRdwStore()

    await store.fetchStatus('VIN1')

    expect(store.available).toBe(true)
    expect(store.hasPlate).toBe(false)
  })

  it('sends a blank plate as none, and takes the answer as the new status', async () => {
    const { rdwApi } = await import('@/services/rdwApi')
    vi.mocked(rdwApi.setLicensePlate).mockResolvedValue({ licensePlate: null, info: null })
    const store = useRdwStore()

    await store.setLicensePlate('VIN1', '   ')

    expect(rdwApi.setLicensePlate).toHaveBeenCalledWith('VIN1', null)
    expect(store.status).toEqual({ licensePlate: null, info: null })
  })

  it('asks once for two callers at the same moment', async () => {
    const { rdwApi } = await import('@/services/rdwApi')
    vi.mocked(rdwApi.get).mockResolvedValue(withPlate)
    const store = useRdwStore()

    await Promise.all([store.ensureLoaded('VIN1'), store.ensureLoaded('VIN1')])
    await store.ensureLoaded('VIN1')

    expect(rdwApi.get).toHaveBeenCalledTimes(1)
  })
})
