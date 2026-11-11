import type { Page } from '@playwright/test'
import { test, expect, open } from './fixtures'

interface DemoStatus {
  latitude: number | null
  longitude: number | null
  [field: string]: unknown
}

async function useParkedStatus(page: Page) {
  let available = true
  // Other browser tests can change the shared demo car while these tests are running.
  await page.route('**/hubs/telemetry/negotiate**', (route) =>
    route.fulfill({ status: 503, body: 'Telemetry disabled for this test' }),
  )
  await page.route(/\/api\/vehicles\/[^/]+\/status$/, async (route) => {
    const response = await route.fetch()
    const status: DemoStatus = await response.json()
    await route.fulfill({
      response,
      json: {
        ...status,
        currentJourneyDistance: 0,
        speed: 0,
        engineRunning: false,
        latitude: available ? status.latitude : null,
        longitude: available ? status.longitude : null,
      },
    })
  })
  return {
    setAvailable(value: boolean) {
      available = value
    },
  }
}

for (const viewport of [
  { name: 'a phone', width: 320, height: 568 },
  { name: 'a desktop', width: 1440, height: 900 },
]) {
  test(`offers directions alongside the dashboard map on ${viewport.name}`, async ({ page }) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height })
    await useParkedStatus(page)
    await open(page, '/')

    const card = page.locator('.location-map-card')
    await expect(card.locator('.location-directions')).toHaveCount(0)
    const opener = card.getByRole('button', { name: 'Directions to car', exact: true })
    await opener.click()
    await expect(page).toHaveURL(/\/$/)
    const dialog = page.getByRole('dialog', { name: 'Directions to car' })
    const link = dialog.getByRole('link', { name: 'Directions to car (Google Maps)' })
    await expect(link).toBeVisible()
    const url = new URL((await link.getAttribute('href'))!)
    expect(Object.fromEntries(url.searchParams)).toEqual({
      api: '1',
      destination: '52.340100,4.945500',
      travelmode: 'walking',
    })

    const appleLink = dialog.getByRole('link', { name: 'Walking directions in Apple Maps' })
    await expect(appleLink).toBeVisible()
    await expect(dialog.getByRole('link', { name: /compatible map app/ })).toBeVisible()
    for (const theme of ['dark', 'light']) {
      const standardButton = await page.evaluate((value) => {
        document.documentElement.dataset.theme = value
        const reference = document.createElement('a')
        reference.className = 'btn btn-outline-secondary'
        document.body.append(reference)
        const style = getComputedStyle(reference)
        const result = { color: style.color, fontSize: style.fontSize, padding: style.padding }
        reference.remove()
        return result
      }, theme)
      await expect(link).toHaveCSS('color', 'rgb(255, 255, 255)')
      await expect(appleLink).toHaveCSS('color', standardButton.color)
      const buttons = dialog.locator('.location-directions .btn')
      await expect(buttons).toHaveCount(5)
      for (const button of await buttons.all()) {
        await expect(button.locator('svg')).toBeVisible()
        await expect(button).toHaveCSS('font-size', standardButton.fontSize)
        await expect(button).toHaveCSS('padding', standardButton.padding)
      }
    }
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
      ),
    ).toBe(true)
    await page.keyboard.press('Escape')
    await expect(dialog).toBeHidden()
    await expect(opener).toBeFocused()

    const mapStatus = page.waitForResponse((response) =>
      /\/api\/vehicles\/[^/]+\/status$/.test(response.url()),
    )
    await card.getByRole('button', { name: 'Open full map' }).click()
    await expect(page).toHaveURL(/\/map$/)
    await mapStatus
  })
}

test('keeps the map popup and manual copy usable on a small phone', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 568 })
  await page.addInitScript(() => {
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: undefined })
  })
  await useParkedStatus(page)
  let releaseAddress!: () => void
  const addressGate = new Promise<void>((resolve) => {
    releaseAddress = resolve
  })
  await page.route('**/api/map/reverse', async (route) => {
    const body: { precision: string; points: unknown[] } = route.request().postDataJSON()
    if (body.precision === 'address') await addressGate
    await route.fulfill({
      json: {
        available: true,
        hasMore: false,
        results: body.points.map(() => ({
          road:
            body.precision === 'address' ? 'A long test street near the old railway station' : null,
          houseNumber: body.precision === 'address' ? '55A' : null,
          city: 'Amsterdam',
          postcode: null,
          countryCode: 'nl',
          displayName: null,
        })),
      },
    })
  })
  const addressRequest = page.waitForRequest((request) => {
    if (!request.url().endsWith('/api/map/reverse')) return false
    const body: { precision: string } = request.postDataJSON()
    return body.precision === 'address'
  })
  const popup = page.locator('.car-popup.leaflet-popup')
  try {
    await page.goto('/map', { waitUntil: 'domcontentloaded' })
    await addressRequest
    await page.locator('.map-canvas').focus()
    await page.locator('.leaflet-marker-icon').first().click()
    await expect(popup.getByRole('button', { name: 'Directions to car' })).toBeVisible()
    await expect(popup.getByRole('link')).toHaveCount(0)
    await expect(popup.locator('.car-popup__address')).toHaveCount(0)
  } finally {
    releaseAddress()
  }
  await expect(popup.locator('.car-popup__address')).toContainText('A long test street')

  await expect
    .poll(async () => {
      const map = await page.locator('.map-wrapper').boundingBox()
      const box = await popup.boundingBox()
      return (
        !!map &&
        !!box &&
        box.x >= map.x - 1 &&
        box.y >= map.y - 1 &&
        box.x + box.width <= map.x + map.width + 1 &&
        box.y + box.height <= map.y + map.height + 1
      )
    })
    .toBe(true)

  await popup.getByRole('button', { name: 'Directions to car' }).click()
  const dialog = page.getByRole('dialog', { name: 'Directions to car' })
  await expect(dialog.getByRole('link', { name: 'Directions to car (Google Maps)' })).toBeVisible()
  await dialog.getByRole('button', { name: 'Copy coordinates' }).click()
  await expect(dialog.getByRole('alert')).toContainText('Could not copy automatically')
  const coordinates = dialog.getByRole('textbox', { name: 'Coordinates to copy' })
  await expect(coordinates).toHaveValue('52.340100,4.945500')
  await coordinates.focus()
  expect(
    await coordinates.evaluate((input: HTMLInputElement) => [
      input.selectionStart,
      input.selectionEnd,
    ]),
  ).toEqual([0, '52.340100,4.945500'.length])
  await dialog.getByRole('button', { name: 'Close', exact: true }).click()
  await expect(dialog).toBeHidden()
  await expect(popup.getByRole('button', { name: 'Directions to car' })).toBeFocused()
})

test('recovers the dashboard map when telemetry coordinates disappear and return', async ({
  page,
}) => {
  const status = await useParkedStatus(page)
  const errors: string[] = []
  page.on('pageerror', (error) => errors.push(error.message))
  page.on('console', (message) => {
    if (message.type() === 'error' && /TypeError|ReferenceError/.test(message.text()))
      errors.push(message.text())
  })
  await open(page, '/')
  const card = page.locator('.location-map-card')
  await card.getByRole('button', { name: 'Directions to car', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: 'Directions to car' })
  await expect(dialog.getByRole('link', { name: 'Directions to car (Google Maps)' })).toBeVisible()

  status.setAvailable(false)
  await page.evaluate(() => document.dispatchEvent(new Event('visibilitychange')))
  await expect(card.locator('.location-map-card__empty')).toBeVisible()
  await expect(dialog.getByRole('link')).toHaveCount(0)

  status.setAvailable(true)
  await page.evaluate(() => document.dispatchEvent(new Event('visibilitychange')))
  await expect(dialog.getByRole('link', { name: 'Directions to car (Google Maps)' })).toBeVisible()
  await expect(card.locator('.leaflet-marker-icon')).toBeVisible()
  expect(errors).toEqual([])
})
