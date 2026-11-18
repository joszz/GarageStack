import { describe, expect, it } from 'vitest'
import { withPlaces } from '@/utils/tripLogPlaces'
import { deventer, logEntry, zwolle } from '@/services/__tests__/tripLogFixtures'

describe('withPlaces', () => {
  it('fills in the ends the server found, and leaves the other trips as they were', () => {
    const first = logEntry({ id: 1 })
    const second = logEntry({ id: 2 })

    const result = withPlaces([first, second], [{ id: 1, startPlace: zwolle, endPlace: deventer }])

    expect(result[0]).toMatchObject({ startPlace: zwolle, endPlace: deventer })
    expect(result[1]).toBe(second)
  })

  it('keeps an end it already had when the server found nothing for it', () => {
    const entry = logEntry({ id: 1, endPlace: deventer })

    const [result] = withPlaces([entry], [{ id: 1, startPlace: zwolle, endPlace: null }])

    expect(result).toMatchObject({ startPlace: zwolle, endPlace: deventer })
  })
})
