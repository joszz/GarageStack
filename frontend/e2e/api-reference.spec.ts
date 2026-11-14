import { test, expect } from './fixtures'

// The API reference (Scalar) is the API's page, passed through nginx with a policy of its own:
// nginx must leave the app's policy off it, or the browser enforces both and blocks the page.
test('serves the API reference with its own policy, not the app policy', async ({ page }) => {
  const response = await page.goto('/api/reference/')

  const headers = response!.headers()
  expect(headers['content-security-policy']).toMatch(/script-src 'self' 'nonce-[^']+'/)
  expect(headers['content-security-policy']).not.toContain('tiles.openfreemap.org')
  expect(headers['x-frame-options']).toBe('DENY')
  expect(headers['x-content-type-options']).toBe('nosniff')
})

test('renders the API reference within its policy', async ({ page, cspViolations }) => {
  await page.goto('/api/reference/')
  await page.waitForLoadState('networkidle')

  await expect(page.getByRole('heading', { name: 'GarageStack API', exact: true })).toBeVisible()
  // Zod, inside Scalar's bundle, tries eval once to see whether it may compile its validators,
  // and validates without it when the policy says no. That one refusal is expected; the policy
  // deliberately has no 'unsafe-eval'.
  const unexpected = cspViolations.filter(
    (v) =>
      !(
        v.directive === 'script-src' &&
        v.blockedUri === 'eval' &&
        v.source.includes('/scalar.js:')
      ),
  )
  expect(unexpected).toEqual([])
})

// Signed out, a browser lands on the login page, which brings it back once signed in.
test('sends a browser that is not signed in to the login page', async ({ browser }) => {
  const context = await browser.newContext({ storageState: { cookies: [], origins: [] } })
  const page = await context.newPage()

  await page.goto('/api/reference/')

  await expect(page).toHaveURL(/\/login\?redirect=%2Fapi%2Freference%2F$/)
  await context.close()
})
