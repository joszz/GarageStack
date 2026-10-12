const opened = new Set<string>()

/**
 * Has the browser set up the connection to another origin (DNS, TCP, TLS) before anything is
 * fetched from it, so the first request there does not wait for the handshake. Once per origin
 * per page load; a hint the page never uses only costs an idle socket for a few seconds.
 *
 * The link carries no crossorigin attribute, so it warms the connection plain image loads use,
 * such as Leaflet's tiles. A fetch() made in CORS mode without credentials would not use it.
 */
export function preconnect(origin: string): void {
  if (opened.has(origin)) return
  opened.add(origin)
  const link = document.createElement('link')
  link.rel = 'preconnect'
  link.href = origin
  document.head.append(link)
}
