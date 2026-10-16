import { test, expect } from './fixtures'

// Runs under the plain-http project: the app reached over HTTP by a name that is not localhost,
// as on a LAN address. The login page needs its scripts to render the form at all.
test('loads the app over plain HTTP on a non-localhost address', async ({
  page,
  cspViolations,
}) => {
  const failed: string[] = []
  page.on('requestfailed', (request) => failed.push(request.url()))

  await page.goto('/login')

  await expect(page.locator('#username')).toBeVisible()
  expect(failed.filter((url) => url.startsWith('https:'))).toEqual([])
  expect(cspViolations).toEqual([])
})
