// Takes the screenshot gallery (documentation/screenshots) and the install screenshots in the
// manifest (public/screenshot-*.webp) from a running demo stack, so they show the app as it is.
//
//   docker compose --env-file .env.demo -f docker-compose.demo.yml up -d --build   (repository root)
//   cd frontend && pnpm screenshots [base URL, http://localhost:8080 by default] [name ...]
//
// Names after the URL (dashboard-edit, settings-vehicle) retake only those screenshots.
//
// Desktop shots are 1280 x 800, phone shots 390 x 844 at twice the density, in the dark theme with
// the date filter on 30 days, in the Netherlands, and one of each in the light theme. CHROMIUM_PATH points at a
// browser other than Playwright's own; HTTPS_PROXY, when set, is where the map's tiles are fetched.
import { copyFile, writeFile } from 'node:fs/promises'
import { chromium } from '@playwright/test'

const base = (process.argv[2] ?? 'http://localhost:8080').replace(/\/$/, '')
const only = new Set(process.argv.slice(3))
const docs = new URL('../../documentation/screenshots/', import.meta.url)
const publicDir = new URL('../public/', import.meta.url)

const DESKTOP = { name: 'desktop', viewport: { width: 1280, height: 800 }, deviceScaleFactor: 1 }
const PHONE = {
  name: 'mobile',
  viewport: { width: 390, height: 844 },
  deviceScaleFactor: 2,
  isMobile: true,
  hasTouch: true,
}

// The pages of the gallery, each with what has to have happened before it is worth a picture.
// `after` undoes a setting the shot saved; `scrolled` keeps the page where `prepare` scrolled it;
// `wide` shots are of what only a desktop shows.
const PAGES = [
  { file: 'login', path: '/login', signedOut: true },
  { file: 'dashboard', path: '/' },
  {
    file: 'dashboard-map-backdrop',
    path: '/',
    wide: true,
    prepare: showMapBackdrop,
    after: hideMapBackdrop,
  },
  { file: 'dashboard-edit', path: '/', prepare: editLayout },
  { file: 'dashboard-offline', path: '/', prepare: goOffline, after: goOnline },
  { file: 'hv-battery', path: '/', prepare: openHvBattery },
  { file: 'map', path: '/map' },
  { file: 'map-trip', path: '/map', prepare: selectTrip },
  { file: 'statistics', path: '/statistics' },
  { file: 'statistics-energy', path: '/statistics', prepare: scrollToEnergy, scrolled: true },
  { file: 'trip-log', path: '/trip-log' },
  { file: 'trip-edit', path: '/trip-log', prepare: openTripEditor },
  { file: 'charge-history', path: '/charge-history', prepare: openChargeSession },
  { file: 'maintenance', path: '/maintenance' },
  { file: 'climate-schedules', path: '/climate-schedules' },
  { file: 'settings', path: '/', prepare: openSettings },
  { file: 'settings-vehicle', path: '/', prepare: openVehicleSettings },
  { file: 'logs', path: '/logs', prepare: showWorkerLog },
]

// The install screenshots in the manifest (vite.config.ts), copied from the gallery's.
const MANIFEST = { dashboard: 'home', map: 'map', statistics: 'statistics' }

const browser = await chromium.launch({
  executablePath: process.env.CHROMIUM_PATH || undefined,
  // A software GPU, so the vector basemap draws in a headless browser instead of staying blank.
  args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'],
  // Playwright sends even localhost through a proxy unless told otherwise.
  proxy: process.env.HTTPS_PROXY
    ? { server: process.env.HTTPS_PROXY, bypass: 'localhost,127.0.0.1' }
    : undefined,
})

try {
  for (const device of [DESKTOP, PHONE]) {
    await shoot(device, 'dark', PAGES)
    await shoot(device, 'light', [{ file: 'dashboard-light', path: '/' }])
  }
  for (const [page, name] of Object.entries(MANIFEST)) {
    for (const device of ['desktop', 'mobile']) {
      await copyFile(
        new URL(`${device}-${page}.webp`, docs),
        new URL(`screenshot-${device}-${name}.webp`, publicDir),
      )
    }
  }
} finally {
  await browser.close()
}

async function shoot(device, theme, allPages) {
  const { name, ...options } = device
  const pages = allPages.filter(
    (shot) => (only.size === 0 || only.has(shot.file)) && !(shot.wide && device.isMobile),
  )
  if (pages.length === 0) return
  // The region follows the browser's time zone: Amsterdam gives the gallery's kilometres, bar and
  // 24-hour clock whatever machine runs this.
  const context = await browser.newContext({
    ...options,
    colorScheme: theme,
    locale: 'en-GB',
    timezoneId: 'Europe/Amsterdam',
  })
  // The install offer would cover the phone screenshots.
  await context.addInitScript(() => {
    localStorage.setItem('garagestack-pwa-install-dismissed', 'never')
  })
  const page = await context.newPage()
  // A control that is not there fails the run instead of waiting for ever.
  page.setDefaultTimeout(30_000)
  // The sign-in page first, while there is no one signed in; it follows the browser's theme.
  for (const shot of pages.filter((shot) => shot.signedOut)) await capture(page, name, shot)
  await signIn(page)
  // Saved to the demo account, which every page then takes: the theme, English, the gallery's
  // 30 days, and a selected trip coloured against the speed limits.
  await saveSettings(page, 'ui', { theme, locale: 'en', filterDays: 30 })
  await saveSettings(page, 'map', { speedLimitOverlayEnabled: true })

  for (const shot of pages.filter((shot) => !shot.signedOut)) await capture(page, name, shot)
  await context.close()
}

async function capture(page, device, shot) {
  await page.goto(base + shot.path)
  await settled(page)
  if (shot.prepare) await shot.prepare(page)
  // A focused control scrolls the page a little on its way in; the gallery shows it from the top.
  if (!shot.scrolled) {
    await page.evaluate(() => document.querySelector('.main-content')?.scrollTo({ top: 0 }))
  }
  const file = `${device}-${shot.file}.webp`
  const png = await page.screenshot()
  if (shot.after) await shot.after(page)
  await writeFile(new URL(file, docs), await webp(page, png))
  console.log(`screenshots: ${file}`)
}

async function signIn(page) {
  await page.goto(`${base}/login`)
  await page.fill('#username', process.env.DEMO_USERNAME ?? 'demo')
  await page.fill('#password', process.env.DEMO_PASSWORD ?? 'demo')
  await Promise.all([page.waitForURL(`${base}/`), page.click('button[type="submit"]')])
}

async function saveSettings(page, section, changes) {
  const status = await page.evaluate(
    async ({ section, changes }) => {
      const response = await fetch(`/api/settings/${section}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify(changes),
      })
      return response.status
    },
    { section, changes },
  )
  if (status >= 300) throw new Error(`Saving the screenshot settings answered ${status}`)
}

// Loaded, skeletons gone, and a moment for the map's tiles and the charts' animations.
async function settled(page) {
  await page.waitForLoadState('networkidle').catch(() => {})
  await page
    .waitForFunction(() => !document.querySelector('.skeleton'), null, { timeout: 20_000 })
    .catch(() => {})
  await page.waitForTimeout(3000)
}

// The car's map across the whole dashboard, the cards over it (Settings > General).
async function showMapBackdrop(page) {
  await saveSettings(page, 'dashboard', { mapBackdrop: true })
  await page.reload()
  await settled(page)
}

async function hideMapBackdrop(page) {
  await saveSettings(page, 'dashboard', { mapBackdrop: false })
}

// The layout editor: with a mouse from the header's button, with Reset beside Done; on a phone
// from a long press on a card, with the bar floating at the bottom.
async function editLayout(page) {
  if (page.viewportSize().width >= 768) {
    await page.getByRole('button', { name: 'Edit layout' }).click()
  } else {
    // The car at the top: the cards below it start under the fold of a phone.
    await longPress(page, page.locator('.overview-row.long-press-cards > *').first())
  }
  await page
    .getByRole('toolbar')
    .or(page.getByRole('button', { name: 'Done' }))
    .first()
    .waitFor()
  // Caught mid-wiggle, the way it looks.
  await page.waitForTimeout(800)
}

// A touch that rests on the element, as a finger does: the pointer events say "touch".
async function longPress(page, locator) {
  const box = await locator.boundingBox()
  const point = { x: box.x + box.width / 2, y: box.y + box.height / 2 }
  const cdp = await page.context().newCDPSession(page)
  await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [point] })
  await page.waitForTimeout(800)
  await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] })
  await cdp.detach()
}

// The car as last loaded, once the connection to GarageStack is gone.
async function goOffline(page) {
  await page.context().setOffline(true)
  await page.getByText('No connection to GarageStack').first().waitFor({ timeout: 30_000 })
  await page.waitForTimeout(500)
}

async function goOnline(page) {
  await page.context().setOffline(false)
}

// The high-voltage battery's details, with the charge limit and target an electric car can set.
async function openHvBattery(page) {
  await page
    .getByRole('button', { name: /HV battery/i })
    .first()
    .click()
  await page.getByRole('dialog').waitFor()
  await page.waitForTimeout(500)
}

// The energy and efficiency charts further down the statistics.
async function scrollToEnergy(page) {
  await page
    .getByText(/Energy used per day/i)
    .first()
    .evaluate((title) => {
      const main = document.querySelector('.main-content')
      const chart = title.closest('.card') ?? title
      const offset = chart.getBoundingClientRect().top - main.getBoundingClientRect().top
      main.scrollBy({ top: offset - 16 })
    })
  await page.waitForTimeout(1500)
}

// One trip, snapped to the roads and coloured against the speed limits, as the gallery shows it.
async function selectTrip(page) {
  const trips = page.locator('.trip-list__button')
  // To Utrecht, not from it: the drive back is a newer trip without a route to snap.
  const toUtrecht = trips.filter({ hasText: /Amsterdam\s*Utrecht/ }).first()
  await ((await toUtrecht.count()) > 0 ? toUtrecht : trips.first()).click()
  await settled(page)
}

// The route editor for the newest trip, opened the way a driver opens it: from the trip's details.
async function openTripEditor(page) {
  await page.locator('.trip-log-item__select').first().click()
  await page.getByRole('link', { name: 'Edit route' }).first().click()
  await page.waitForURL(/\/trips\/\d+\/edit$/)
  await settled(page)
  // The map is new here, so its vector basemap starts from nothing: give its tiles time to draw.
  await page.waitForTimeout(5000)
}

// On a desktop the newest charge session opened on its power curve and where it charged; a phone
// has no room for both that and the list, so it shows the list.
async function openChargeSession(page) {
  if (page.viewportSize().width < 768) return
  await page.locator('.charge-history-item__toggle').first().click()
  await settled(page)
  // The session's map is new, so give its tiles time to draw.
  await page.waitForTimeout(5000)
}

// The settings dialog on its first tab.
async function openSettings(page) {
  await page.getByRole('button', { name: 'Settings', exact: true }).first().click()
  await page.getByRole('tablist').waitFor()
  await page.waitForTimeout(500)
}

// The Vehicle tab: the car's type, its own battery, tyre and alert levels.
async function openVehicleSettings(page) {
  await openSettings(page)
  await page.getByRole('tab', { name: 'Vehicle' }).click()
  await page.waitForTimeout(500)
}

// The Worker's entries: the newest of all are the requests this script itself just made.
async function showWorkerLog(page) {
  await page.getByRole('button', { name: 'Worker', exact: true }).click()
  await settled(page)
}

// The browser encodes WebP itself, so the script needs no image library.
async function webp(page, png) {
  const dataUrl = await page.evaluate(
    async (source) => {
      const image = new Image()
      image.src = source
      await image.decode()
      const canvas = document.createElement('canvas')
      canvas.width = image.naturalWidth
      canvas.height = image.naturalHeight
      canvas.getContext('2d').drawImage(image, 0, 0)
      return canvas.toDataURL('image/webp', 0.85)
    },
    `data:image/png;base64,${png.toString('base64')}`,
  )
  return Buffer.from(dataUrl.slice(dataUrl.indexOf(',') + 1), 'base64')
}
