import { Resvg } from '@resvg/resvg-js'
import { writeFileSync } from 'fs'
import { fileURLToPath } from 'url'
import { dirname, join } from 'path'

const __dirname = dirname(fileURLToPath(import.meta.url))

const BG = '#0f1117'
const FG = '#3b82f6'

// Car path original coordinate space: 512 x 512.
// Content spans y≈32–490 (centre ≈ 261). We centre that in the canvas and
// bias slightly upward so the result feels visually balanced.
// Adaptive icon safe zone = circle radius 40% of image size. The car bounding
// box corners are at ~344 original units from centre, so scale is capped at
// size*0.38/344 to keep all corners inside the circle with breathing room.
// All icons, maskable and non-maskable, use the same layout so they are
// visually identical regardless of which one the OS or browser picks.
const carPath =
  'M135.2 117.4L109.1 192H402.9l-26.1-74.6C372.3 104.6 360.2 96 346.6 96H165.4c-13.6 0-25.7 8.6-30.2 21.4zM39.6 196.8L74.8 96.3C88.3 57.8 124.6 32 165.4 32H346.6c40.8 0 77.1 25.8 90.6 64.3l35.2 100.5c23.2 9.6 39.6 32.5 39.6 59.2V400v48c0 17.7-14.3 32-32 32H448c-17.7 0-32-14.3-32-32V400H96v48c0 17.7-14.3 32-32 32H32c-17.7 0-32-14.3-32-32V400 256c0-26.7 16.4-49.6 39.6-59.2zM128 288a32 32 0 1 0-64 0 32 32 0 1 0 64 0zm288 32a32 32 0 1 0 0-64 32 32 0 1 0 0 64z'

function buildIconSvg(size) {
  const scale = (size * 0.38) / 344

  const contentCenterY = 261
  const carTy = (size / 2 - contentCenterY * scale - size * 0.02).toFixed(2)
  const carTx = (size / 2 - 256 * scale).toFixed(2)
  const textY = (parseFloat(carTy) + 490 * scale).toFixed(2)
  const fontSize = Math.round(size * 0.13)

  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${size} ${size}">
  <rect width="${size}" height="${size}" fill="${BG}"/>
  <path transform="translate(${carTx},${carTy}) scale(${scale.toFixed(4)})" fill="${FG}"
    d="${carPath}"/>
  <text x="${size / 2}" y="${textY}" text-anchor="middle"
        font-family="Arial,sans-serif" font-size="${fontSize}"
        font-weight="700" letter-spacing="-1" fill="${FG}">GS</text>
</svg>`
}

// Android draws a notification badge from its alpha channel alone, tinting it to suit the status
// bar, so an opaque icon shows as a solid square. The badge is therefore the car alone, in white on
// transparency: no background, and no "GS", which would be unreadable at status-bar size.
// The car spans x 0–512, y 32–480 in its own coordinate space.
function buildBadgeSvg(size) {
  const scale = (size * 0.9) / 512
  const tx = ((size - 512 * scale) / 2).toFixed(2)
  const ty = ((size - 448 * scale) / 2 - 32 * scale).toFixed(2)

  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${size} ${size}">
  <path transform="translate(${tx},${ty}) scale(${scale.toFixed(4)})" fill="#ffffff"
    d="${carPath}"/>
</svg>`
}

// Scales the icon path to fill ~72% of the canvas, then centres it.
// vbW/vbH are the coordinate space dimensions of the FA path.
function buildShortcutSvg(size, iconPath, vbW, vbH) {
  const available = size * 0.72
  const scale = Math.min(available / vbW, available / vbH)
  const scaledW = vbW * scale
  const scaledH = vbH * scale
  const tx = ((size - scaledW) / 2).toFixed(2)
  const ty = ((size - scaledH) / 2).toFixed(2)

  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${size} ${size}">
  <rect width="${size}" height="${size}" fill="${BG}"/>
  <path transform="translate(${tx},${ty}) scale(${scale.toFixed(4)})" fill="${FG}"
    d="${iconPath}"/>
</svg>`
}

// FA location-dot: viewBox 0 0 384 512
const mapIconPath =
  'M215.7 499.2C267 435 384 279.4 384 192C384 86 298 0 192 0S0 86 0 192c0 87.4 117 243 168.3 307.2c12.3 15.3 35.1 15.3 47.4 0zM192 128a64 64 0 1 1 0 128 64 64 0 1 1 0-128z'

// FA chart-column (vertical bars): viewBox 0 0 448 512
const statsIconPath =
  'M160 80c0-26.5 21.5-48 48-48h32c26.5 0 48 21.5 48 48V432c0 26.5-21.5 48-48 48H208c-26.5 0-48-21.5-48-48V80zM0 272c0-26.5 21.5-48 48-48H80c26.5 0 48 21.5 48 48V432c0 26.5-21.5 48-48 48H48c-26.5 0-48-21.5-48-48V272zM368 96h32c26.5 0 48 21.5 48 48V432c0 26.5-21.5 48-48 48H368c-26.5 0-48-21.5-48-48V144c0-26.5 21.5-48 48-48z'

// FA book: viewBox 0 0 448 512
const tripLogIconPath =
  'M384 512L96 512c-53 0-96-43-96-96L0 96C0 43 43 0 96 0L400 0c26.5 0 48 21.5 48 48l0 288c0 20.9-13.4 38.7-32 45.3l0 66.7c17.7 0 32 14.3 32 32s-14.3 32-32 32l-32 0zM96 384c-17.7 0-32 14.3-32 32s14.3 32 32 32l256 0 0-64-256 0zm32-232c0 13.3 10.7 24 24 24l176 0c13.3 0 24-10.7 24-24s-10.7-24-24-24l-176 0c-13.3 0-24 10.7-24 24zm24 72c-13.3 0-24 10.7-24 24s10.7 24 24 24l176 0c13.3 0 24-10.7 24-24s-10.7-24-24-24l-176 0z'

// FA calendar-days: viewBox 0 0 448 512
const climateIconPath =
  'M128 0c17.7 0 32 14.3 32 32l0 32 128 0 0-32c0-17.7 14.3-32 32-32s32 14.3 32 32l0 32 32 0c35.3 0 64 28.7 64 64l0 288c0 35.3-28.7 64-64 64L64 480c-35.3 0-64-28.7-64-64L0 128C0 92.7 28.7 64 64 64l32 0 0-32c0-17.7 14.3-32 32-32zM64 240l0 32c0 8.8 7.2 16 16 16l32 0c8.8 0 16-7.2 16-16l0-32c0-8.8-7.2-16-16-16l-32 0c-8.8 0-16 7.2-16 16zm128 0l0 32c0 8.8 7.2 16 16 16l32 0c8.8 0 16-7.2 16-16l0-32c0-8.8-7.2-16-16-16l-32 0c-8.8 0-16 7.2-16 16zm144-16c-8.8 0-16 7.2-16 16l0 32c0 8.8 7.2 16 16 16l32 0c8.8 0 16-7.2 16-16l0-32c0-8.8-7.2-16-16-16l-32 0zM64 368l0 32c0 8.8 7.2 16 16 16l32 0c8.8 0 16-7.2 16-16l0-32c0-8.8-7.2-16-16-16l-32 0c-8.8 0-16 7.2-16 16zm144-16c-8.8 0-16 7.2-16 16l0 32c0 8.8 7.2 16 16 16l32 0c8.8 0 16-7.2 16-16l0-32c0-8.8-7.2-16-16-16l-32 0zm112 16l0 32c0 8.8 7.2 16 16 16l32 0c8.8 0 16-7.2 16-16l0-32c0-8.8-7.2-16-16-16l-32 0c-8.8 0-16 7.2-16 16z'

// FA screwdriver-wrench: viewBox 0 0 576 512
const maintenanceIconPath =
  'M70.8-6.7c5.4-5.4 13.8-6.2 20.2-2L209.9 70.5c8.9 5.9 14.2 15.9 14.2 26.6l0 49.6 90.8 90.8c33.3-15 73.9-8.9 101.2 18.5L542.2 382.1c18.7 18.7 18.7 49.1 0 67.9l-60.1 60.1c-18.7 18.7-49.1 18.7-67.9 0L288.1 384c-27.4-27.4-33.5-67.9-18.5-101.2l-90.8-90.8-49.6 0c-10.7 0-20.7-5.3-26.6-14.2L23.4 58.9c-4.2-6.3-3.4-14.8 2-20.2L70.8-6.7zm145 303.5c-6.3 36.9 2.3 75.9 26.2 107.2l-94.9 95c-28.1 28.1-73.7 28.1-101.8 0s-28.1-73.7 0-101.8l135.4-135.5 35.2 35.1zM384.1 0c20.1 0 39.4 3.7 57.1 10.5 10 3.8 11.8 16.5 4.3 24.1L388.8 91.3c-3 3-4.7 7.1-4.7 11.3l0 41.4c0 8.8 7.2 16 16 16l41.4 0c4.2 0 8.3-1.7 11.3-4.7l56.7-56.7c7.6-7.5 20.3-5.7 24.1 4.3 6.8 17.7 10.5 37 10.5 57.1 0 43.2-17.2 82.3-45 111.1l-49.1-49.1c-33.1-33-78.5-45.7-121.1-38.4l-56.8-56.8 0-29.7-.2-5c-.8-12.4-4.4-24.3-10.5-34.9 29.4-35 73.4-57.2 122.7-57.3z'

// FA file-lines: viewBox 0 0 384 512
const logsIconPath =
  'M0 64C0 28.7 28.7 0 64 0L213.5 0c17 0 33.3 6.7 45.3 18.7L365.3 125.3c12 12 18.7 28.3 18.7 45.3L384 448c0 35.3-28.7 64-64 64L64 512c-35.3 0-64-28.7-64-64L0 64zm208-5.5l0 93.5c0 13.3 10.7 24 24 24L325.5 176 208 58.5zM120 256c-13.3 0-24 10.7-24 24s10.7 24 24 24l144 0c13.3 0 24-10.7 24-24s-10.7-24-24-24l-144 0zm0 96c-13.3 0-24 10.7-24 24s10.7 24 24 24l144 0c13.3 0 24-10.7 24-24s-10.7-24-24-24l-144 0z'

function write(filePath, svg, size, background = BG) {
  const resvg = new Resvg(svg, {
    fitTo: { mode: 'width', value: size },
    ...(background ? { background } : {}),
  })
  const png = resvg.render().asPng()
  writeFileSync(filePath, png)
  console.log(`Written: ${filePath}`)
}

const pub = join(__dirname, '..', 'public')

// App icons: full-bleed dark background, purpose: "any maskable" in the manifest
write(join(pub, 'pwa-64x64.png'), buildIconSvg(64), 64)
write(join(pub, 'pwa-192x192.png'), buildIconSvg(192), 192)
write(join(pub, 'pwa-512x512.png'), buildIconSvg(512), 512)
write(join(pub, 'apple-touch-icon-180x180.png'), buildIconSvg(180), 180)

// Shortcut icons
write(join(pub, 'shortcut-map-96x96.png'), buildShortcutSvg(96, mapIconPath, 384, 512), 96)
write(join(pub, 'shortcut-stats-96x96.png'), buildShortcutSvg(96, statsIconPath, 448, 512), 96)
write(join(pub, 'shortcut-trip-log-96x96.png'), buildShortcutSvg(96, tripLogIconPath, 448, 512), 96)
write(join(pub, 'shortcut-climate-96x96.png'), buildShortcutSvg(96, climateIconPath, 448, 512), 96)
write(
  join(pub, 'shortcut-maintenance-96x96.png'),
  buildShortcutSvg(96, maintenanceIconPath, 576, 512),
  96,
)
write(join(pub, 'shortcut-logs-96x96.png'), buildShortcutSvg(96, logsIconPath, 384, 512), 96)

// Notification badge: transparent, so Android can draw its shape (96px is Android's badge size)
write(join(pub, 'badge-96x96.png'), buildBadgeSvg(96), 96, null)
