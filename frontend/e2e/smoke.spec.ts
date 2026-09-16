import { test, expect, open } from './fixtures'

// An icon that loses its stylesheet keeps only the SVG's own aspect ratio and grows to the
// width of its container, which is how the blocked FontAwesome stylesheet showed up: icons
// filled entire cards. Anything in a normal text range is fine.
const MAX_ICON_HEIGHT_PX = 40

test('serves the security headers on the app page itself', async ({ page }) => {
  const response = await page.goto('/login')

  const headers = response!.headers()
  expect(headers['content-security-policy']).toContain("default-src 'none'")
  expect(headers['x-frame-options']).toBe('DENY')
  expect(headers['x-content-type-options']).toBe('nosniff')
})

test('renders the dashboard within the policy', async ({ page, cspViolations }) => {
  await open(page, '/')

  await expect(page.locator('.status-grid .status-card').first()).toBeVisible()
  expect(cspViolations).toEqual([])
})

test('renders icons at text size, not at their container size', async ({ page }) => {
  await open(page, '/')
  await expect(page.locator('.status-card__icon svg').first()).toBeVisible()

  const height = await page
    .locator('.status-card__icon svg')
    .first()
    .evaluate((el) => el.getBoundingClientRect().height)

  expect(height).toBeGreaterThan(0)
  expect(height).toBeLessThan(MAX_ICON_HEIGHT_PX)
})

test('rotates the live car marker to the vehicle heading', async ({ page, cspViolations }) => {
  // The demo vehicle is mid-trip, so the dashboard's location preview shows the car marker.
  await open(page, '/')

  const marker = page.locator('.trip-marker--active').first()
  await expect(marker).toBeVisible()
  // Blocked by style-src-attr when the rotation comes from markup rather than the CSSOM.
  await expect(marker).toHaveAttribute('style', /rotate\(-?\d+(\.\d+)?deg\)/)
  expect(cspViolations).toEqual([])
})

test('opens the map view within the policy', async ({ page, cspViolations }) => {
  await open(page, '/map')

  await expect(page.locator('.map-canvas')).toBeVisible()
  await expect(page.locator('.trip-list__item').first()).toBeVisible()
  expect(cspViolations).toEqual([])
})

test('renders the statistics charts within the policy', async ({ page, cspViolations }) => {
  await open(page, '/statistics')

  await expect(page.locator('.chart-container canvas').first()).toBeVisible()
  expect(cspViolations).toEqual([])
})

test('opens the climate modal with a usable Apply button', async ({ page }) => {
  await open(page, '/')
  await page
    .locator('.status-card', { hasText: /climate/i })
    .first()
    .click()

  const footer = page.locator('.detail-modal__footer')
  await expect(footer.getByRole('button', { name: /apply|toepassen/i })).toBeVisible()
})
