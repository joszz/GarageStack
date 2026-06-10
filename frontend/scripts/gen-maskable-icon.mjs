import { Resvg } from '@resvg/resvg-js'
import { writeFileSync } from 'fs'
import { fileURLToPath } from 'url'
import { dirname, join } from 'path'

const __dirname = dirname(fileURLToPath(import.meta.url))

// Full-bleed background: entire 512x512 canvas filled with the app dark color.
// Content is scaled to fit within the safe zone (central 80% = 410x410 area).
// Safe zone: from (51,51) to (461,461) -- content padded ~10% on each side.
const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">
  <rect width="512" height="512" fill="#1a1d27"/>
  <path transform="translate(73, 95) scale(0.72)" fill="#3b82f6"
    d="M135.2 117.4L109.1 192H402.9l-26.1-74.6C372.3 104.6 360.2 96 346.6 96H165.4
       c-13.6 0-25.7 8.6-30.2 21.4zM39.6 196.8L74.8 96.3C88.3 57.8 124.6 32 165.4 32H346.6
       c40.8 0 77.1 25.8 90.6 64.3l35.2 100.5c23.2 9.6 39.6 32.5 39.6 59.2V400v48
       c0 17.7-14.3 32-32 32H448c-17.7 0-32-14.3-32-32V400H96v48c0 17.7-14.3 32-32 32H32
       c-17.7 0-32-14.3-32-32V400 256c0-26.7 16.4-49.6 39.6-59.2zM128 288a32 32 0 1 0-64 0
       32 32 0 1 0 64 0zm288 32a32 32 0 1 0 0-64 32 32 0 1 0 0 64z"/>
  <text x="256" y="475" text-anchor="middle" font-family="system-ui,sans-serif"
        font-size="66" font-weight="700" letter-spacing="-1" fill="#3b82f6">GS</text>
</svg>`

const resvg = new Resvg(svg, {
  fitTo: { mode: 'width', value: 512 },
})
const png = resvg.render().asPng()

const outPath = join(__dirname, '..', 'public', 'maskable-icon-512x512.png')
writeFileSync(outPath, png)
console.log(`Written: ${outPath}`)
