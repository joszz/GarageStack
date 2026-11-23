/**
 * Hands the browser a CSV file to save. It leads with a UTF-8 byte order mark, without which
 * Excel reads the file in the system's legacy code page and garbles accented place names.
 */
export function downloadCsv(filename: string, csv: string): void {
  downloadBlob(filename, new Blob(['﻿', csv], { type: 'text/csv;charset=utf-8' }))
}

/** Hands the browser a GPX track to save. */
export function downloadGpx(filename: string, gpx: string): void {
  downloadBlob(filename, new Blob([gpx], { type: 'application/gpx+xml' }))
}

function downloadBlob(filename: string, blob: Blob): void {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = filename
  document.body.append(link)
  link.click()
  link.remove()
  // Revoked on the next tick: some browsers start the download only after the click handler returns.
  setTimeout(() => URL.revokeObjectURL(url), 0)
}
