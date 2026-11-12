import { afterEach, describe, expect, it, vi } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import nl from '@/locales/nl.json'
import es from '@/locales/es.json'
import pt from '@/locales/pt.json'
import type { CarLocation } from '@/utils/carLocation'
import LocationDirections from '../LocationDirections.vue'
import { installFontAwesome } from '@/plugins/fontAwesome'

enableAutoUnmount(afterEach)

const location: CarLocation = {
  vehicleId: 1,
  latitude: 52.09,
  longitude: 5.12,
  mayBeMoving: false,
  vehicleUpdatedAt: '2026-10-19T10:00:00Z',
}

function mountDirections(
  point: CarLocation | null = location,
  locale: 'en' | 'nl' | 'es' | 'pt' = 'en',
) {
  const i18n = createI18n({ legacy: false, locale, messages: { en, nl, es, pt } })
  return mount(LocationDirections, {
    props: { location: point, open: true },
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true, Teleport: true } },
  })
}

const copyButton = (wrapper: ReturnType<typeof mountDirections>) =>
  wrapper.findAll('button').find((button) => button.text() === en.directions.copy)!

describe('LocationDirections', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('renders an icon for every option using standard dialog buttons', () => {
    const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
    const wrapper = mount(LocationDirections, {
      props: { location, open: true },
      global: { plugins: [i18n, installFontAwesome], stubs: { Teleport: true } },
    })
    const buttons = wrapper.findAll('.location-directions .btn')

    expect(buttons).toHaveLength(5)
    expect(buttons.map((button) => button.get('svg').attributes('data-icon'))).toEqual([
      'map',
      'google',
      'apple',
      'mobile-screen',
      'copy',
    ])
    expect(
      buttons.slice(1, 3).map((button) => button.get('svg').attributes('data-prefix')),
    ).toEqual(['fab', 'fab'])
    for (const button of buttons) {
      expect(button.classes()).not.toContain('btn-sm')
      expect(button.get('svg').attributes('aria-hidden')).toBe('true')
    }
  })

  it('offers OpenStreetMap walking directions first, alternatives after, without browser location', () => {
    const getCurrentPosition = vi.fn<() => void>()
    vi.stubGlobal('navigator', { geolocation: { getCurrentPosition } })
    const wrapper = mountDirections()
    const links = wrapper.findAll('a')
    const osm = links.find((link) => link.text() === en.directions.osm)!
    const google = links.find((link) => link.text() === en.directions.google)!
    const apple = links.find((link) => link.text() === en.directions.apple)!
    const app = links.find((link) => link.text() === en.directions.mapApp)!

    expect(wrapper.get('.location-directions__primary').element).toBe(osm.element)
    expect(new URL(osm.attributes('href')!).searchParams.get('to')).toBe('52.090000,5.120000')
    expect(osm.attributes('target')).toBe('_blank')
    expect(osm.attributes('rel')).toBe('noopener noreferrer')
    expect(osm.attributes('aria-describedby')).toBe(
      wrapper.find('.location-directions__note').attributes('id'),
    )
    expect(new URL(google.attributes('href')!).searchParams.get('destination')).toBe(
      '52.090000,5.120000',
    )
    expect(google.attributes('target')).toBe('_blank')
    expect(google.attributes('rel')).toBe('noopener noreferrer')
    expect(apple.attributes('href')).toContain('mode=walking')
    expect(apple.attributes('target')).toBe('_blank')
    expect(app.attributes('href')).toBe('geo:52.090000,5.120000?q=52.090000,5.120000(Car)')
    expect(app.attributes('target')).toBeUndefined()
    expect(app.attributes('rel')).toBeUndefined()
    expect(wrapper.text()).toContain('the location may be older')
    expect(wrapper.text()).toContain(en.directions.shareNotice)
    expect(getCurrentPosition).not.toHaveBeenCalled()
  })

  it.each(['en', 'nl', 'es', 'pt'] as const)('shows the controls in %s', (locale) => {
    const wrapper = mountDirections(location, locale)
    const messages = { en, nl, es, pt }[locale]

    expect(wrapper.text()).toContain(messages.directions.osm)
    expect(wrapper.text()).toContain(messages.directions.google)
    expect(wrapper.get('[role="dialog"]').attributes('aria-label')).toBe(messages.directions.open)
    expect(wrapper.text()).toContain(messages.directions.mapApp)
  })

  it('updates the dialog when its language changes', async () => {
    const i18n = createI18n({ legacy: false, locale: 'en', messages: { en, nl, es, pt } })
    const wrapper = mount(LocationDirections, {
      props: { location, open: true },
      global: { plugins: [i18n], stubs: { FontAwesomeIcon: true, Teleport: true } },
    })

    i18n.global.locale.value = 'nl'
    await flushPromises()

    expect(wrapper.text()).toContain(nl.directions.osm)
    expect(wrapper.get('[role="dialog"]').attributes('aria-label')).toBe(nl.directions.open)
  })

  it('only shows options while open and emits close from the dialog', async () => {
    const wrapper = mountDirections()
    await wrapper.get('.dialog-close').trigger('click')
    expect(wrapper.emitted('close')).toHaveLength(1)
    await wrapper.setProps({ open: false })
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
    expect(wrapper.find('a').exists()).toBe(false)
  })

  it('explains unavailable and possibly moving locations instead of exposing links', async () => {
    const wrapper = mountDirections(null)
    expect(wrapper.text()).toContain(en.directions.unavailable)
    expect(wrapper.get('.location-directions__primary').attributes('disabled')).toBeDefined()
    expect(wrapper.find('a').exists()).toBe(false)

    await wrapper.setProps({ location: { ...location, mayBeMoving: true } })
    expect(wrapper.text()).toContain(en.directions.moving)
    expect(wrapper.get('.location-directions__primary').attributes('disabled')).toBeDefined()
    expect(wrapper.find('a').exists()).toBe(false)
  })

  it('labels an unknown update time without presenting it as a GPS-fix time', () => {
    const wrapper = mountDirections({ ...location, vehicleUpdatedAt: null })
    expect(wrapper.text()).toContain(en.directions.lastKnownUnknown)
  })

  it('copies coordinates on request and reports success', async () => {
    const writeText = vi.fn<(_: string) => Promise<void>>().mockResolvedValue()
    vi.stubGlobal('navigator', { clipboard: { writeText } })
    const wrapper = mountDirections()

    await copyButton(wrapper).trigger('click')
    await flushPromises()

    expect(writeText).toHaveBeenCalledExactlyOnceWith('52.090000,5.120000')
    expect(wrapper.find('[role="status"]').text()).toBe(en.directions.copied)
  })

  it.each(['missing', 'rejected'])(
    'reports a %s clipboard and offers selectable coordinates',
    async (kind) => {
      const clipboard =
        kind === 'missing'
          ? undefined
          : { writeText: vi.fn<(_: string) => Promise<void>>().mockRejectedValue(new Error('no')) }
      vi.stubGlobal('navigator', { clipboard })
      const wrapper = mountDirections()

      await copyButton(wrapper).trigger('click')
      await flushPromises()

      expect(wrapper.find('[role="alert"]').text()).toBe(en.directions.copyFailed)
      const input = wrapper.get('input')
      expect(input.element.value).toBe('52.090000,5.120000')
      expect(input.attributes('readonly')).toBeDefined()
      expect(input.attributes('aria-label')).toBe(en.directions.coordinates)
      const select = vi.spyOn(input.element, 'select')
      await input.trigger('focus')
      expect(select).toHaveBeenCalledOnce()
    },
  )

  it('does not report an old destination as copied after the car changes', async () => {
    let finishCopy: (() => void) | undefined
    const writeText = vi.fn<(_: string) => Promise<void>>(
      () =>
        new Promise<void>((resolve) => {
          finishCopy = resolve
        }),
    )
    vi.stubGlobal('navigator', { clipboard: { writeText } })
    const wrapper = mountDirections()

    await copyButton(wrapper).trigger('click')
    await wrapper.setProps({ location: { ...location, vehicleId: 2, latitude: 51 } })
    finishCopy?.()
    await flushPromises()

    expect(wrapper.text()).not.toContain(en.directions.copied)
    expect(wrapper.get('.location-directions__primary').attributes('href')).toContain('51.000000')
  })

  it.each(['success', 'failure'] as const)(
    'does not show a previous copy %s after the dialog is reopened',
    async (result) => {
      let finishCopy!: () => void
      let failCopy!: (error: Error) => void
      const writeText = vi.fn<(_: string) => Promise<void>>(
        () =>
          new Promise<void>((resolve, reject) => {
            finishCopy = resolve
            failCopy = reject
          }),
      )
      vi.stubGlobal('navigator', { clipboard: { writeText } })
      const wrapper = mountDirections()

      await copyButton(wrapper).trigger('click')
      await wrapper.setProps({ open: false })
      await wrapper.setProps({ open: true })
      if (result === 'success') finishCopy()
      else failCopy(new Error('clipboard rejected'))
      await flushPromises()

      expect(wrapper.text()).not.toContain(en.directions.copied)
      expect(wrapper.find('[role="alert"]').exists()).toBe(false)
    },
  )
})
