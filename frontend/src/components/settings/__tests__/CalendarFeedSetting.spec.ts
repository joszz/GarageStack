import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import nl from '@/locales/nl.json'
import type { CalendarLink, CreatedCalendarLink } from '@/services/calendarApi'
import CalendarFeedSetting from '../CalendarFeedSetting.vue'

enableAutoUnmount(afterEach)

const api = vi.hoisted(() => ({
  status: vi.fn<() => Promise<CalendarLink>>(),
  create: vi.fn<() => Promise<CreatedCalendarLink>>(),
  stop: vi.fn<() => Promise<void>>(),
}))
vi.mock('@/services/calendarApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/services/calendarApi')>()),
  calendarApi: api,
}))

const none: CalendarLink = { exists: false, createdAt: null, lastUsedAt: null }
const active: CalendarLink = {
  exists: true,
  createdAt: '2026-11-01T08:00:00Z',
  lastUsedAt: '2026-11-18T06:00:00Z',
}
const made: CreatedCalendarLink = {
  details: { exists: true, createdAt: '2026-11-18T09:00:00Z', lastUsedAt: null },
  token: 'gs_calendar-secret',
}

async function render(locale: 'en' | 'nl' = 'en') {
  const i18n = createI18n({ legacy: false, locale, messages: { en, nl } })
  const wrapper = mount(CalendarFeedSetting, {
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true } },
  })
  await flushPromises()
  return wrapper
}

const button = (wrapper: Awaited<ReturnType<typeof render>>, text: string) =>
  wrapper.findAll('button').find((b) => b.text() === text)!

describe('CalendarFeedSetting', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    api.status.mockReset().mockResolvedValue(none)
    api.create.mockReset().mockResolvedValue(made)
    api.stop.mockReset().mockResolvedValue()
  })

  it('offers to make a link when there is none', async () => {
    const wrapper = await render()

    expect(button(wrapper, en.settings.calendarFeed.create).exists()).toBe(true)
    expect(wrapper.text()).not.toContain(en.settings.calendarFeed.stop)
  })

  it('shows a new link once, on this address and in the page language, with a subscribe link', async () => {
    const wrapper = await render('nl')

    await button(wrapper, nl.settings.calendarFeed.create).trigger('click')
    await flushPromises()

    const url = new URL(wrapper.get('input').element.value)
    expect(url.origin + url.pathname).toBe(`${window.location.origin}/api/calendar/feed.ics`)
    expect(url.searchParams.get('token')).toBe('gs_calendar-secret')
    expect(url.searchParams.get('culture')).toBe('nl')
    const subscribe = wrapper.get('a.calendar-feed__subscribe')
    expect(subscribe.attributes('href')).toBe(url.href.replace(/^https?:/, 'webcal:'))
  })

  it('says a link is active, without showing it, when one was made before', async () => {
    api.status.mockResolvedValue(active)
    const wrapper = await render()

    expect(wrapper.text()).toContain(en.settings.calendarFeed.active)
    expect(wrapper.text()).toContain('last fetched')
    expect(wrapper.find('input').exists()).toBe(false)
  })

  it('replaces an active link only on a second tap', async () => {
    api.status.mockResolvedValue(active)
    const wrapper = await render()

    await button(wrapper, en.settings.calendarFeed.replace).trigger('click')
    expect(api.create).not.toHaveBeenCalled()
    await button(wrapper, en.settings.calendarFeed.confirmReplace).trigger('click')
    await flushPromises()

    expect(api.create).toHaveBeenCalledOnce()
    expect(wrapper.get('input').element.value).toContain('gs_calendar-secret')
  })

  it('stops the link on a second tap, and offers to make one again', async () => {
    api.status.mockResolvedValue(active)
    const wrapper = await render()

    await button(wrapper, en.settings.calendarFeed.stop).trigger('click')
    expect(api.stop).not.toHaveBeenCalled()
    await button(wrapper, en.settings.calendarFeed.confirmStop).trigger('click')
    await flushPromises()

    expect(api.stop).toHaveBeenCalledOnce()
    expect(button(wrapper, en.settings.calendarFeed.create).exists()).toBe(true)
  })

  it('copies the link', async () => {
    const writeText = vi.fn<(text: string) => Promise<void>>().mockResolvedValue()
    vi.stubGlobal('navigator', { clipboard: { writeText } })
    const wrapper = await render()
    await button(wrapper, en.settings.calendarFeed.create).trigger('click')
    await flushPromises()

    await button(wrapper, en.settings.calendarFeed.copy).trigger('click')
    await flushPromises()

    expect(writeText).toHaveBeenCalledWith(wrapper.get('input').element.value)
    expect(wrapper.text()).toContain(en.settings.calendarFeed.copied)
    vi.unstubAllGlobals()
  })

  it('shows why the link could not be made', async () => {
    api.create.mockRejectedValue(new Error('offline'))
    const wrapper = await render()

    await button(wrapper, en.settings.calendarFeed.create).trigger('click')
    await flushPromises()

    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
  })
})
