import { Resvg } from '@resvg/resvg-js'
import { writeFileSync } from 'fs'
import { fileURLToPath } from 'url'
import { dirname, join } from 'path'

const __dirname = dirname(fileURLToPath(import.meta.url))

const BG = '#1a1d27'
const FG = '#3b82f6'

// Car path original coordinate space: 512 x 512.
// Content spans y≈32–490 (centre ≈ 261). We centre that in the canvas and
// bias slightly upward so the result feels visually balanced.
// Adaptive icon safe zone = circle radius 40% of image size. The car bounding
// box corners are at ~344 original units from centre, so scale is capped at
// size*0.38/344 to keep all corners inside the circle with breathing room.
function buildMaskableSvg(size) {
  const scale = (size * 0.38) / 344

  // Center the car+text block (y=32–490) in the canvas
  const contentCenterY = 261
  const carTy = (size / 2 - contentCenterY * scale - size * 0.02).toFixed(2)
  const carTx = (size / 2 - 256 * scale).toFixed(2)
  const textY = (parseFloat(carTy) + 490 * scale).toFixed(2)
  const fontSize = Math.round(size * 0.13)

  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${size} ${size}">
  <rect width="${size}" height="${size}" fill="${BG}"/>
  <path transform="translate(${carTx},${carTy}) scale(${scale.toFixed(4)})" fill="${FG}"
    d="M135.2 117.4L109.1 192H402.9l-26.1-74.6C372.3 104.6 360.2 96 346.6 96H165.4
       c-13.6 0-25.7 8.6-30.2 21.4zM39.6 196.8L74.8 96.3C88.3 57.8 124.6 32 165.4 32H346.6
       c40.8 0 77.1 25.8 90.6 64.3l35.2 100.5c23.2 9.6 39.6 32.5 39.6 59.2V400v48
       c0 17.7-14.3 32-32 32H448c-17.7 0-32-14.3-32-32V400H96v48c0 17.7-14.3 32-32 32H32
       c-17.7 0-32-14.3-32-32V400 256c0-26.7 16.4-49.6 39.6-59.2zM128 288a32 32 0 1 0-64 0
       32 32 0 1 0 64 0zm288 32a32 32 0 1 0 0-64 32 32 0 1 0 0 64z"/>
  <text x="${size / 2}" y="${textY}" text-anchor="middle"
        font-family="system-ui,sans-serif" font-size="${fontSize}"
        font-weight="700" letter-spacing="-1" fill="${FG}">GS</text>
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

// FA location-dot  — viewBox 0 0 384 512
const mapIconPath =
  'M215.7 499.2C267 435 384 279.4 384 192C384 86 298 0 192 0S0 86 0 192c0 87.4 117 243 168.3 307.2c12.3 15.3 35.1 15.3 47.4 0zM192 128a64 64 0 1 1 0 128 64 64 0 1 1 0-128z'

// FA chart-column (vertical bars) — viewBox 0 0 448 512
const statsIconPath =
  'M160 80c0-26.5 21.5-48 48-48h32c26.5 0 48 21.5 48 48V432c0 26.5-21.5 48-48 48H208c-26.5 0-48-21.5-48-48V80zM0 272c0-26.5 21.5-48 48-48H80c26.5 0 48 21.5 48 48V432c0 26.5-21.5 48-48 48H48c-26.5 0-48-21.5-48-48V272zM368 96h32c26.5 0 48 21.5 48 48V432c0 26.5-21.5 48-48 48H368c-26.5 0-48-21.5-48-48V144c0-26.5 21.5-48 48-48z'

function write(filePath, svg, size) {
  const resvg = new Resvg(svg, {
    fitTo: { mode: 'width', value: size },
    background: BG,
  })
  const png = resvg.render().asPng()
  writeFileSync(filePath, png)
  console.log(`Written: ${filePath}`)
}

const pub = join(__dirname, '..', 'public')

write(join(pub, 'maskable-icon-512x512.png'), buildMaskableSvg(512), 512)
write(join(pub, 'maskable-icon-192x192.png'), buildMaskableSvg(192), 192)
write(join(pub, 'shortcut-map-96x96.png'), buildShortcutSvg(96, mapIconPath, 384, 512), 96)
write(join(pub, 'shortcut-stats-96x96.png'), buildShortcutSvg(96, statsIconPath, 448, 512), 96)
