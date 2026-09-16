import { test as setup, expect } from '@playwright/test'
import { AUTH_STATE_FILE } from './auth-state'

/**
 * Signs in once and stores the session for every other test. Besides being faster, this keeps
 * the suite clear of the API's login rate limit, which a per-test sign-in would trip as soon as
 * a run is retried.
 */
setup('signs in with the demo account', async ({ page }) => {
  await page.goto('/login')
  await page.fill('#username', 'demo')
  await page.fill('#password', 'demo')
  await page.click('button[type=submit]')

  await expect(page.locator('.status-grid .status-card').first()).toBeVisible()

  await page.context().storageState({ path: AUTH_STATE_FILE })
})
