import { Resvg } from '@resvg/resvg-js'
import { writeFileSync } from 'fs'
import { fileURLToPath } from 'url'
import { dirname, join } from 'path'

const __dirname = dirname(fileURLToPath(import.meta.url))

function buildMaskableSvg(size) {
  // Safe zone = central 80%. For each size: pad = size * 0.10
  const pad = size * 0.1
  const inner = size - pad * 2
  // Car path is drawn on a 512x512 coordinate system; scale to fit inner area
  const carScale = inner / 512
  const carTx = pad
  const carTy = pad + inner * 0.1 // slight top bias to keep "GS" inside safe zone
  const textY = size - pad * 0.6
  const fontSize = Math.round(size * 0.13)

  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${size} ${size}">
  <rect width="${size}" height="${size}" fill="#1a1d27"/>
  <path transform="translate(${carTx}, ${carTy}) scale(${carScale.toFixed(4)})" fill="#3b82f6"
    d="M135.2 117.4L109.1 192H402.9l-26.1-74.6C372.3 104.6 360.2 96 346.6 96H165.4
       c-13.6 0-25.7 8.6-30.2 21.4zM39.6 196.8L74.8 96.3C88.3 57.8 124.6 32 165.4 32H346.6
       c40.8 0 77.1 25.8 90.6 64.3l35.2 100.5c23.2 9.6 39.6 32.5 39.6 59.2V400v48
       c0 17.7-14.3 32-32 32H448c-17.7 0-32-14.3-32-32V400H96v48c0 17.7-14.3 32-32 32H32
       c-17.7 0-32-14.3-32-32V400 256c0-26.7 16.4-49.6 39.6-59.2zM128 288a32 32 0 1 0-64 0
       32 32 0 1 0 64 0zm288 32a32 32 0 1 0 0-64 32 32 0 1 0 0 64z"/>
  <text x="${size / 2}" y="${textY}" text-anchor="middle" font-family="system-ui,sans-serif"
        font-size="${fontSize}" font-weight="700" letter-spacing="-1" fill="#3b82f6">GS</text>
</svg>`
}

// shortcut icon: map pin (Font Awesome location-dot path, simplified)
function buildShortcutSvg(size, iconPath) {
  const pad = size * 0.18
  const inner = size - pad * 2
  const scale = inner / 384
  const tx = pad
  const ty = pad

  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${size} ${size}">
  <rect width="${size}" height="${size}" fill="#1a1d27"/>
  <path transform="translate(${tx}, ${ty}) scale(${scale.toFixed(4)})" fill="#3b82f6"
    d="${iconPath}"/>
</svg>`
}

// FA location-dot (map pin) viewBox 0 0 384 512
const mapIconPath =
  'M215.7 499.2C267 435 384 279.4 384 192C384 86 298 0 192 0S0 86 0 192c0 87.4 117 243 168.3 307.2c12.3 15.3 35.1 15.3 47.4 0zM192 128a64 64 0 1 1 0 128 64 64 0 1 1 0-128z'

// FA chart-bar viewBox 0 0 512 512
const statsIconPath =
  'M32 32c17.7 0 32 14.3 32 32V400c0 8.8 7.2 16 16 16H480c17.7 0 32 14.3 32 32s-14.3 32-32 32H80c-44.2 0-80-35.8-80-80V64C0 46.3 14.3 32 32 32zm96 96c0-17.7 14.3-32 32-32l192 0c17.7 0 32 14.3 32 32s-14.3 32-32 32l-192 0c-17.7 0-32-14.3-32-32zm32 160c-17.7 0-32-14.3-32-32s14.3-32 32-32H320c17.7 0 32 14.3 32 32s-14.3 32-32 32H160zm192-96c0-17.7 14.3-32 32-32s32 14.3 32 32V352c0 17.7-14.3 32-32 32s-32-14.3-32-32V192z'

function write(filePath, svg, size) {
  const resvg = new Resvg(svg, { fitTo: { mode: 'width', value: size } })
  const png = resvg.render().asPng()
  writeFileSync(filePath, png)
  console.log(`Written: ${filePath}`)
}

const pub = join(__dirname, '..', 'public')

write(join(pub, 'maskable-icon-512x512.png'), buildMaskableSvg(512), 512)
write(join(pub, 'maskable-icon-192x192.png'), buildMaskableSvg(192), 192)
write(join(pub, 'shortcut-map-96x96.png'), buildShortcutSvg(96, mapIconPath), 96)
write(join(pub, 'shortcut-stats-96x96.png'), buildShortcutSvg(96, statsIconPath), 96)
