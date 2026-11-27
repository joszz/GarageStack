import { describe, it, expect } from 'vitest'
import {
  isCategoryShown,
  isPerCarCategory,
  notificationCategoryId,
  notificationCategoryIdsFor,
  NOTIFICATION_CATEGORY_IDS,
  vehicleExclusionsOf,
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

  it('groups the RDW categories, which carry a recall code or an APK date, under "rdw"', () => {
    expect(notificationCategoryId('rdw-recall-MGP230061')).toBe('rdw')
    expect(notificationCategoryId('rdw-apk-due-30-20261108')).toBe('rdw')
    expect(notificationCategoryId('rdw-uninsured')).toBe('rdw')
  })

  it.each(NOTIFICATION_CATEGORY_IDS.filter((id) => id !== 'maintenance'))(
    'maps the fixed backend category "%s" to itself',
    (id) => {
      expect(notificationCategoryId(id)).toBe(id)
    },
  )
})

describe('notificationCategoryIdsFor', () => {
  it('drops the plug-in only categories for a plain hybrid, keeping the order of the rest', () => {
    expect(notificationCategoryIdsFor('hev')).toEqual(
      NOTIFICATION_CATEGORY_IDS.filter(
        (id) => id !== 'low-ev' && id !== 'charging-complete' && id !== 'charging-interrupted',
      ),
    )
  })

  it.each(['hev', 'phev', 'bev', 'unknown'] as const)(
    'offers MG app messages for %s, since every drivetrain has the app',
    (type) => {
      expect(notificationCategoryIdsFor(type)).toContain('vehicle-message')
    },
  )

  it('drops low fuel for a BEV, keeping the order of the rest', () => {
    expect(notificationCategoryIdsFor('bev')).toEqual(
      NOTIFICATION_CATEGORY_IDS.filter((id) => id !== 'low-fuel'),
    )
  })

  it.each(['phev', 'unknown'] as const)('offers every category for %s', (type) => {
    expect(notificationCategoryIdsFor(type)).toEqual([...NOTIFICATION_CATEGORY_IDS])
  })
})

describe('isCategoryShown', () => {
  it('shows an opt-in type only once switched on, and any other until switched off', () => {
    expect(isCategoryShown('weekly-summary', [], [])).toBe(false)
    expect(isCategoryShown('weekly-summary', [], ['weekly-summary'])).toBe(true)
    expect(isCategoryShown('engine-start', [], [])).toBe(true)
    expect(isCategoryShown('engine-start', ['engine-start'], [])).toBe(false)
    expect(isCategoryShown(null, ['engine-start'], [])).toBe(true)
  })

  // The same rule as the server's NotificationCategories.IsWanted: either list keeps a type out.
  it('leaves out a type switched off for the car, and only for that car', () => {
    expect(isCategoryShown('doors-open-parked', [], [], ['doors-open-parked'])).toBe(false)
    expect(isCategoryShown('doors-open-parked', [], [], [])).toBe(true)
    expect(isCategoryShown('doors-open-parked', [], [], ['low-tyre'])).toBe(true)
    expect(isCategoryShown('low-tyre', [], [], ['doors-open-parked'])).toBe(true)
  })

  it('keeps a type out when the device has it off, whatever the car says', () => {
    expect(isCategoryShown('engine-start', ['engine-start'], [], [])).toBe(false)
    expect(isCategoryShown('engine-start', ['engine-start'], [], ['engine-start'])).toBe(false)
  })

  it('lets a car switch a summary off once the device has asked for it', () => {
    expect(isCategoryShown('weekly-summary', [], ['weekly-summary'], [])).toBe(true)
    expect(isCategoryShown('weekly-summary', [], ['weekly-summary'], ['weekly-summary'])).toBe(
      false,
    )
    expect(isCategoryShown('weekly-summary', [], [], [])).toBe(false)
  })

  it('shows a notification without a type whatever a car has off', () => {
    expect(isCategoryShown(null, [], [], ['engine-start'])).toBe(true)
  })
})

describe('isPerCarCategory', () => {
  // The MG sign-in failing is about the account: the Worker sends it without a car.
  it('is false only for the types the Worker sends without naming a car', () => {
    expect(NOTIFICATION_CATEGORY_IDS.filter((id) => !isPerCarCategory(id))).toEqual([
      'gateway-login',
    ])
  })
})

describe('vehicleExclusionsOf', () => {
  const byVehicle = { '7': ['low-tyre'], '9': ['engine-start', 'maintenance'] }

  it('returns the types switched off for that car', () => {
    expect(vehicleExclusionsOf(byVehicle, 9)).toEqual(['engine-start', 'maintenance'])
  })

  it('returns none for a car the device never chose for', () => {
    expect(vehicleExclusionsOf(byVehicle, 8)).toEqual([])
  })

  // A notification about no car (the MG sign-in failing) answers to the device's own list alone.
  it.each([null, undefined])('returns none for a notification without a car (%s)', (vehicleId) => {
    expect(vehicleExclusionsOf(byVehicle, vehicleId)).toEqual([])
  })
})
