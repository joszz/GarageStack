// Writes a gzip copy beside every compressible file of the build, for nginx's gzip_static
// (frontend/nginx-app.conf): compressed once here at the highest level instead of at nginx's
// default level 1 on every response. Usage: node scripts/precompress.mjs [dir], dist by default.
import { readdir, readFile, stat, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { constants, gzipSync } from 'node:zlib'

const COMPRESSIBLE = /\.(?:js|mjs|css|html|svg|json|webmanifest|txt)$/
// Below nginx's gzip_min_length the header costs more than the bytes it saves.
const MIN_BYTES = 1024

async function* files(dir) {
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name)
    if (entry.isDirectory()) yield* files(path)
    else if (entry.isFile()) yield path
  }
}

const root = process.argv[2] ?? 'dist'
let written = 0
let before = 0
let after = 0
for await (const path of files(root)) {
  if (!COMPRESSIBLE.test(path) || (await stat(path)).size < MIN_BYTES) continue
  const source = await readFile(path)
  const compressed = gzipSync(source, { level: constants.Z_BEST_COMPRESSION })
  // A file that does not shrink is better sent as it is.
  if (compressed.length >= source.length) continue
  await writeFile(`${path}.gz`, compressed)
  written += 1
  before += source.length
  after += compressed.length
}

const kib = (bytes) => `${(bytes / 1024).toFixed(0)} KiB`
console.log(`precompress: ${written} files, ${kib(before)} -> ${kib(after)} gzipped`)
