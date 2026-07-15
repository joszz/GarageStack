import { describe, it, expect } from 'vitest'
import { notificationCategoryId, NOTIFICATION_CATEGORY_IDS } from '@/utils/notificationCategories'

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
