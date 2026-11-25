import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ViewHeader from '../ViewHeader.vue'
import en from '@/locales/en.json'
import { PHONE_MEDIA_QUERY } from '@/utils/viewport'

vi.mock('@/components/OfflineNotice.vue', () => ({ default: { template: '<div />' } }))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountHeader(props: { title: string; caption?: string; subtitle?: string }) {
  return mount(ViewHeader, {
    props,
    attachTo: document.body,
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true } },
  })
}

describe('ViewHeader', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    document.body.innerHTML = ''
  })

  it('shows the heading and caption in its own row off a phone', () => {
    const wrapper = mountHeader({ title: 'Map', caption: '27 Aug - 25 Nov 2026' })

    expect(wrapper.get('.view-header h1').text()).toBe('Map')
    expect(wrapper.get('.view-header__caption').text()).toBe('27 Aug - 25 Nov 2026')
  })

  it('offers no info button for a page without a line about it', () => {
    const wrapper = mountHeader({ title: 'Map' })

    expect(wrapper.find('.view-header__info').exists()).toBe(false)
  })

  it('opens the line about the page from its info button', async () => {
    const wrapper = mountHeader({ title: 'Trip log', subtitle: 'Every trip, for the taxman.' })
    const info = wrapper.get('.view-header__info')
    expect(info.attributes('aria-label')).toBe(en.common.aboutPage)
    expect(document.body.textContent).not.toContain('Every trip, for the taxman.')

    await info.trigger('click')

    const dialog = document.querySelector('[role="dialog"]')
    expect(dialog?.getAttribute('aria-label')).toBe('Trip log')
    expect(dialog?.textContent).toContain('Every trip, for the taxman.')
  })

  it('puts the heading in the top bar on a phone, leaving the caption in the row', async () => {
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === PHONE_MEDIA_QUERY,
      media: query,
      addEventListener: () => {},
      removeEventListener: () => {},
    }))
    const topbar = document.body.appendChild(document.createElement('div'))
    topbar.id = 'mobile-topbar-title'

    const wrapper = mountHeader({ title: 'Map', caption: '27 Aug - 25 Nov 2026' })
    await flushPromises()

    expect(topbar.querySelector('h1')?.textContent).toBe('Map')
    expect(wrapper.find('.view-header h1').exists()).toBe(false)
    expect(wrapper.get('.view-header__caption').text()).toBe('27 Aug - 25 Nov 2026')
  })
})
