import { describe, expect, it } from 'vitest'
import { preconnect } from '@/utils/preconnect'

function linksTo(origin: string): HTMLLinkElement[] {
  return [...document.head.querySelectorAll<HTMLLinkElement>('link[rel="preconnect"]')].filter(
    (link) => link.getAttribute('href') === origin,
  )
}

describe('preconnect', () => {
  it('adds a preconnect hint for the origin', () => {
    preconnect('https://tiles.one.test')

    const [link] = linksTo('https://tiles.one.test')
    expect(link).toBeDefined()
    // Tiles are plain image loads, which use the connection a hint without crossorigin opens.
    expect(link!.hasAttribute('crossorigin')).toBe(false)
  })

  it('adds the hint once per origin, however often it is asked for', () => {
    preconnect('https://tiles.two.test')
    preconnect('https://tiles.two.test')

    expect(linksTo('https://tiles.two.test')).toHaveLength(1)
  })
})
