import { describe, it, expect } from 'vitest'
import {
  notificationCategoryId,
  notificationCategoryIdsFor,
  NOTIFICATION_CATEGORY_IDS,
} from '@/utils/notificationCategories'

describe('notificationCategoryId', () => {
  it('returns null for a null category', () => {
    expect(notificationCategoryId(null)).toBeNull()
  })

  it('returns null for an unrecognised category', () => {
    expect(notificationCategoryId('some-future-category')).toBeNull()
  })

  it('groups any maintenance-<id> category under "maintenance"', () => {
    expect(notificationCategoryId('maintenance-42')).toBe('maintenance')
    expect(notificationCategoryId('maintenance-oil-change')).toBe('maintenance')
  })

  it.each(NOTIFICATION_CATEGORY_IDS.filter((id) => id !== 'maintenance'))(
    'maps the fixed backend category "%s" to itself',
    (id) => {
      expect(notificationCategoryId(id)).toBe(id)
    },
  )
})

describe('notificationCategoryIdsFor', () => {
  it('drops the plug-in only categories for a plain hybrid', () => {
    const ids = notificationCategoryIdsFor('hev')

    expect(ids).not.toContain('charging-complete')
    expect(ids).not.toContain('low-ev')
    expect(ids).toContain('engine-start')
    expect(ids).toHaveLength(NOTIFICATION_CATEGORY_IDS.length - 2)
  })

  it('keeps the order of the remaining categories', () => {
    expect(notificationCategoryIdsFor('hev')).toEqual(
      NOTIFICATION_CATEGORY_IDS.filter((id) => id !== 'low-ev' && id !== 'charging-complete'),
    )
  })

  it.each(['hev', 'phev', 'bev', 'unknown'] as const)(
    'offers MG app messages for %s, since every drivetrain has the app',
    (type) => {
      expect(notificationCategoryIdsFor(type)).toContain('vehicle-message')
    },
  )

  it.each(['phev', 'bev', 'unknown'] as const)('offers every category for %s', (type) => {
    expect(notificationCategoryIdsFor(type)).toEqual([...NOTIFICATION_CATEGORY_IDS])
  })
})
