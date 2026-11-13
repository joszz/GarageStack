import type { Locator, Page } from '@playwright/test'
import { test, expect, open } from './fixtures'

// Service-worker-owned requests bypass Playwright's API mocks.
test.use({ serviceWorkers: 'block' })

// The narrowest phones trim the tabs to 0.5rem a side so all six fit; 0.625rem everywhere else.
async function expectTabPadding(tab: Locator, viewportWidth: number) {
  const minimum = viewportWidth < 360 ? 8 : 10
  const padding = await tab.evaluate((element) => {
    const style = getComputedStyle(element)
    return { left: parseFloat(style.paddingLeft), right: parseFloat(style.paddingRight) }
  })
  expect(padding.left).toBeGreaterThanOrEqual(minimum)
  expect(padding.right).toBeGreaterThanOrEqual(minimum)
}

async function configureSettings(page: Page, dawarich = true, rdw = true) {
  await page.route('**/api/settings', (route) =>
    route.fulfill({ json: { ui: { locale: 'en', vehicleTypeOverride: 'hev' } } }),
  )
  await page.route('**/api/settings/*', (route) => route.fulfill({ status: 204 }))
  await page.route('**/hubs/telemetry/negotiate**', (route) =>
    route.fulfill({ status: 503, body: 'Live updates disabled for this test' }),
  )
  await page.route('**/api/dawarich/status', (route) =>
    route.fulfill({ json: { configured: dawarich, status: dawarich ? 'ok' : 'disabled' } }),
  )
  await page.route('**/api/vehicles/*/phone-trackers', (route) =>
    route.fulfill({
      json: {
        available: [{ trackerId: 'test-phone', lastSeenAt: '2026-01-01T00:00:00Z' }],
        linked: [{ trackerId: 'test-phone', createdAt: '2026-01-01T00:00:00Z' }],
        error: null,
      },
    }),
  )
  await page.route('**/api/vehicles/*/rdw', (route) =>
    route.fulfill(
      rdw
        ? { json: { licensePlate: null, info: null } }
        : { status: 503, json: { code: 'rdw.disabled' } },
    ),
  )
}

for (const viewport of [
  { width: 320, height: 568 },
  { width: 375, height: 667 },
  { width: 568, height: 320 },
  { width: 768, height: 1024 },
  { width: 840, height: 900 },
  { width: 1024, height: 768 },
  { width: 1440, height: 900 },
]) {
  test(`keeps settings tabs usable at ${viewport.width}x${viewport.height}`, async ({
    page,
    cspViolations,
  }) => {
    await page.setViewportSize(viewport)
    await configureSettings(page)
    await open(page, '/')
    const opener = page.locator('.app-footer__btn:has(svg[data-icon="gear"])')
    await opener.focus()
    await opener.press('Enter')
    const dialog = page.getByRole('dialog')
    const tabs = dialog.getByRole('tablist')
    const tabsViewport = dialog.locator('.settings-tabs-viewport')
    const title = dialog.locator('.detail-modal__title')
    const body = dialog.locator('.detail-modal__body')
    const close = dialog.getByRole('button', { name: 'Close', exact: true })
    await expect(tabs.getByRole('tab')).toHaveCount(6)
    await expect(tabs.locator('[data-tab=phone]')).toHaveAccessibleName('Phone GPS')
    await expect(dialog).toHaveCSS('transform', 'none')
    const titleBox = (await title.boundingBox())!
    const viewportBox = (await tabsViewport.boundingBox())!
    const closeBox = (await close.boundingBox())!
    const middle = (box: { y: number; height: number }) => box.y + box.height / 2
    const tabRowOverflow = () =>
      tabsViewport.evaluate((element) => element.scrollWidth - element.clientWidth)
    // On a phone the tabs get a row of their own under the title and the close button, and all
    // six fit in it whichever is chosen, so none hides past the edge.
    const phoneLayout = viewport.width < 768
    if (phoneLayout) {
      expect(Math.abs(middle(titleBox) - middle(closeBox))).toBeLessThanOrEqual(1)
      expect(viewportBox.y).toBeGreaterThanOrEqual(closeBox.y + closeBox.height - 1)
      expect(viewportBox.x).toBeCloseTo(titleBox.x, 0)
      expect(viewportBox.x + viewportBox.width).toBeCloseTo(closeBox.x + closeBox.width, 0)
    } else {
      expect(Math.abs(middle(titleBox) - middle(viewportBox))).toBeLessThanOrEqual(1)
      expect(titleBox.x + titleBox.width).toBeLessThanOrEqual(viewportBox.x)
      expect(viewportBox.x + viewportBox.width).toBeLessThanOrEqual(closeBox.x)
    }
    const header = dialog.locator('.detail-modal__header')
    await expect(header).toHaveCSS('border-bottom-style', 'solid')
    expect((await header.boundingBox())!.x).toBeCloseTo(titleBox.x, 0)
    const headerBox = (await header.boundingBox())!
    expect(headerBox.y + headerBox.height).toBeCloseTo(viewportBox.y + viewportBox.height, 0)
    expect((await dialog.locator('.settings-panels').boundingBox())!.x).toBeCloseTo(titleBox.x, 0)
    await expect(body).toHaveCSS('padding-inline-end', '16px')
    await expect(dialog).toHaveCSS('padding-inline-end', '16px')
    await expect(body).toHaveCSS('scrollbar-gutter', 'stable')
    if (viewport.width >= 840) {
      expect((await dialog.boundingBox())!.width).toBeCloseTo(771, 0)
      expect(await tabRowOverflow()).toBeLessThanOrEqual(1)
      const lastTabBox = (await tabs.locator('[data-tab=data]').boundingBox())!
      expect(lastTabBox.x + lastTabBox.width).toBeCloseTo(viewportBox.x + viewportBox.width, 0)
    }
    const initialTabTop = viewportBox.y
    await body.evaluate((element) => {
      element.scrollTop = element.scrollHeight
    })
    await expect(close).toBeVisible()
    expect((await tabsViewport.boundingBox())!.y).toBeCloseTo(initialTabTop, 0)

    for (const name of ['general', 'vehicle', 'map', 'phone', 'notifications', 'data']) {
      const tab = tabs.locator(`[data-tab="${name}"]`)
      await tab.click()
      await expect(tab).toHaveAttribute('aria-selected', 'true')
      await expect(tabs.locator('[aria-selected=true]')).toHaveCount(1)
      await expect(dialog.getByRole('tabpanel')).toHaveCount(1)
      expect(await body.evaluate((element) => element.scrollTop)).toBe(0)
      expect((await tabsViewport.boundingBox())!.y).toBeCloseTo(initialTabTop, 0)
      if (phoneLayout) expect(await tabRowOverflow()).toBeLessThanOrEqual(1)
      const buttonBox = (await tab.boundingBox())!
      const tabsBox = (await tabsViewport.boundingBox())!
      expect(buttonBox.height).toBeGreaterThanOrEqual(44)
      await expectTabPadding(tab, viewport.width)
      expect(buttonBox.x).toBeGreaterThanOrEqual(tabsBox.x - 1)
      expect(buttonBox.x + buttonBox.width).toBeLessThanOrEqual(tabsBox.x + tabsBox.width + 1)
      const box = (await dialog.boundingBox())!
      expect(box.x).toBeGreaterThanOrEqual(0)
      expect(box.y).toBeGreaterThanOrEqual(0)
      expect(box.x + box.width).toBeLessThanOrEqual(viewport.width + 1)
      expect(box.y + box.height).toBeLessThanOrEqual(viewport.height + 1)
      if (name === 'phone') {
        await dialog.getByRole('combobox').press('ArrowDown')
        const dropdown = page.locator('.multiselect-dropdown')
        await expect(dropdown).toBeVisible()
        await tab.focus()
        await tab.press('ArrowRight')
        await expect(dropdown).toBeHidden()
      }
    }

    const general = tabs.locator('[data-tab=general]')
    await tabs.locator('[data-tab=data]').press('Home')
    await expect(general).toBeFocused()
    await close.focus()
    await page.keyboard.press('Shift+Tab')
    await expect(general).toBeFocused()
    await page.keyboard.press('Tab')
    await expect(close).toBeFocused()
    await general.focus()
    await page.keyboard.press('Shift+Tab')
    await expect(dialog.getByRole('tabpanel').locator('select').last()).toBeFocused()
    await page.keyboard.press('Tab')
    await expect(general).toBeFocused()

    await general.click()
    for (const locale of ['nl', 'es']) {
      await dialog.locator('#footer-toggle-lang').selectOption(locale)
      await expect(tabs.locator('[data-tab=notifications]')).toHaveText(
        locale === 'nl' ? 'Meldingen' : 'Notificaciones',
      )
      await expect(title).toHaveText(locale === 'nl' ? 'Instellingen' : 'Ajustes')
      await expect(dialog).toHaveAttribute(
        'aria-label',
        locale === 'nl' ? 'Instellingen' : 'Ajustes',
      )
      const phone = tabs.locator('[data-tab=phone]')
      const phoneLabel = locale === 'nl' ? 'Telefoon-GPS' : 'GPS del teléfono'
      await expect(phone).toHaveText('GPS')
      await expect(phone).toHaveAccessibleName(phoneLabel)
      await expect(phone).toHaveAttribute('title', phoneLabel)
      for (const tab of await tabs.getByRole('tab').all())
        await expectTabPadding(tab, viewport.width)
      if (viewport.width >= 840) {
        expect(await tabRowOverflow()).toBeLessThanOrEqual(1)
      } else {
        await phone.click()
        const phoneBox = (await phone.boundingBox())!
        const scrollerBox = (await tabsViewport.boundingBox())!
        expect(phoneBox.x).toBeGreaterThanOrEqual(scrollerBox.x - 1)
        expect(phoneBox.x + phoneBox.width).toBeLessThanOrEqual(
          scrollerBox.x + scrollerBox.width + 1,
        )
        await general.click()
      }
    }
    await expect(tabs.locator('[data-tab=notifications]')).toHaveText('Notificaciones')
    await general.focus()
    await page.keyboard.press('End')
    await expect(tabs.locator('[data-tab=data]')).toBeFocused()
    await expect(dialog.getByRole('tabpanel')).toBeVisible()
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
      ),
    ).toBe(true)
    expect(cspViolations).toEqual([])
    await page.keyboard.press('Escape')
    await expect(dialog).toBeHidden()
    await expect(opener).toBeFocused()
  })
}

test.describe('on a touch phone', () => {
  test.use({
    viewport: { width: 320, height: 568 },
    deviceScaleFactor: 2,
    isMobile: true,
    hasTouch: true,
  })

  test('swipes the tab row and selects the last tab', async ({ page, browserName }) => {
    test.skip(browserName !== 'chromium', 'Touch gestures use Chrome DevTools Protocol')
    await configureSettings(page)
    await open(page, '/')
    await page.getByRole('button', { name: 'Settings', exact: true }).tap()
    const dialog = page.getByRole('dialog', { name: 'Settings' })
    await expect(dialog).toHaveCSS('transform', 'none')
    const scroller = dialog.locator('.settings-tabs-viewport')
    const box = (await scroller.boundingBox())!
    const startX = Math.round(box.x + box.width - 30)
    const endX = Math.round(box.x + 30)
    const y = Math.round(box.y + box.height / 2)
    const client = await page.context().newCDPSession(page)

    for (let swipe = 0; swipe < 6; swipe++) {
      await client.send('Input.dispatchTouchEvent', {
        type: 'touchStart',
        touchPoints: [{ x: startX, y }],
      })
      for (let step = 1; step <= 6; step++) {
        await client.send('Input.dispatchTouchEvent', {
          type: 'touchMove',
          touchPoints: [{ x: Math.round(startX - ((startX - endX) * step) / 6), y }],
        })
        await page.waitForTimeout(40)
      }
      await client.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] })
    }

    await expect
      .poll(() =>
        scroller.evaluate(
          (element) => element.scrollWidth - element.clientWidth - element.scrollLeft,
        ),
      )
      .toBeLessThanOrEqual(1)
    const data = dialog.getByRole('tab', { name: 'Data & API', exact: true })
    await data.tap()
    await expect(data).toHaveAttribute('aria-selected', 'true')
    await expect(dialog.getByRole('tabpanel', { name: 'Data & API', exact: true })).toBeVisible()
    await client.detach()
  })
})

test('themes the settings tabs for light and dark settings', async ({ page }) => {
  await configureSettings(page)
  await open(page, '/')
  await page.getByRole('button', { name: 'Settings', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: 'Settings' })
  const general = dialog.getByRole('tab', { name: 'General', exact: true })
  const tabs = dialog.getByRole('tablist')
  const themeChoices = dialog.getByRole('group', { name: 'Appearance', exact: true })
  for (const [label, theme] of [
    ['Dark', 'dark'],
    ['Light', 'light'],
  ] as const) {
    const choice = themeChoices.getByRole('button', { name: label, exact: true })
    await choice.click()
    await expect(choice).toHaveAttribute('aria-pressed', 'true')
    await expect(page.locator('html')).toHaveAttribute('data-theme', theme)
    const background = await dialog.evaluate((element) => getComputedStyle(element).backgroundColor)
    const border = await tabs.evaluate((element) => getComputedStyle(element).borderBottomColor)
    await expect(tabs).toHaveCSS('border-bottom-style', 'solid')
    await expect(tabs).toHaveCSS('border-bottom-width', '1px')
    await expect(general).toHaveCSS('background-color', background)
    await expect(general).toHaveCSS('border-top-style', 'solid')
    await expect(general).toHaveCSS('border-top-color', border)
    await expect(general).not.toHaveCSS('border-top-left-radius', '0px')
    await dialog.getByRole('button', { name: 'Close', exact: true }).focus()
    await page.keyboard.press('Shift+Tab')
    await expect(general).toBeFocused()
    await expect(general).toHaveCSS('outline-style', 'solid')
  }
})

test('keeps drivetrain and deployment conditions working within settings tabs', async ({
  page,
}) => {
  await configureSettings(page, false, false)
  await open(page, '/')
  await page.getByRole('button', { name: 'Settings', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: 'Settings' })
  await expect(dialog.getByRole('tab')).toHaveCount(5)
  await expect(dialog.locator('[data-tab=phone]')).toHaveCount(0)
  for (const type of ['bev', 'phev', 'hev']) {
    await dialog.locator('[data-tab=vehicle]').click()
    await expect(dialog.locator('.license-plate-form')).toHaveCount(0)
    await dialog.locator('#settings-vehicle-type').selectOption(type)
    await dialog.locator('[data-tab=notifications]').click()
    const labels = dialog.locator('.notif-type-checklist__label')
    await expect(labels.filter({ hasText: 'Charging complete' })).toHaveCount(
      type === 'hev' ? 0 : 1,
    )
    await expect(labels.filter({ hasText: 'Low fuel' })).toHaveCount(type === 'bev' ? 0 : 1)
    await expect(labels.filter({ hasText: 'APK, recalls and insurance (RDW)' })).toHaveCount(0)
  }
})

test('moves focus to an available tab when the selected integration becomes disabled', async ({
  page,
}) => {
  await configureSettings(page)
  let configured = true
  await page.route('**/api/dawarich/status', (route) =>
    route.fulfill({ json: { configured, status: configured ? 'ok' : 'disabled' } }),
  )
  await open(page, '/')
  await page.getByRole('button', { name: 'Settings', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: 'Settings' })
  await expect(dialog).toHaveCSS('transform', 'none')
  await dialog.getByRole('tab', { name: 'Phone GPS', exact: true }).click()
  configured = false
  await dialog.getByRole('button', { name: 'Refresh connection and trackers' }).click()
  await expect(dialog.getByRole('tab', { name: 'Phone GPS', exact: true })).toHaveCount(0)
  await expect(dialog.getByRole('tab', { name: 'General', exact: true })).toBeFocused()
  await expect(dialog.getByRole('tabpanel', { name: 'General', exact: true })).toBeVisible()
})
