import { afterEach, describe, expect, it, vi } from 'vitest'
import { gatewayApi } from '@/services/gatewayApi'
import { useGatewayStatus } from '../useGatewayStatus'

vi.mock('@/services/gatewayApi', () => ({
  gatewayApi: { get: vi.fn<() => Promise<{ signedIn: boolean | null }>>() },
}))

const get = vi.mocked(gatewayApi.get)

describe('useGatewayStatus', () => {
  afterEach(() => {
    useGatewayStatus().apply({ signedIn: null })
    get.mockReset()
  })

  it('takes what the API answers', async () => {
    get.mockResolvedValue({ signedIn: false })
    const gateway = useGatewayStatus()

    await gateway.refresh()

    expect(gateway.signedIn.value).toBe(false)
  })

  it('keeps what it knew when the API cannot be asked', async () => {
    const gateway = useGatewayStatus()
    gateway.apply({ signedIn: true })
    get.mockRejectedValue(new Error('offline'))

    await gateway.refresh()

    expect(gateway.signedIn.value).toBe(true)
  })

  // The sidebar and the demo panel read the same answer.
  it('shares one answer across the app', () => {
    useGatewayStatus().apply({ signedIn: false })

    expect(useGatewayStatus().signedIn.value).toBe(false)
  })
})
