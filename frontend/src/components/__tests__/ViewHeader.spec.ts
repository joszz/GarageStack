import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import { h, nextTick } from 'vue'
import ViewHeader from '../ViewHeader.vue'
import en from '@/locales/en.json'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { PHONE_MEDIA_QUERY } from '@/utils/viewport'

vi.mock('@/components/OfflineNotice.vue', () => ({ default: { template: '<div />' } }))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
let pinia: Pinia

function mountHeader(
  props: { title: string; caption?: string; subtitle?: string },
  actions?: () => ReturnType<typeof h>,
) {
  return mount(ViewHeader, {
    props,
    slots: actions ? { actions } : {},
    attachTo: document.body,
    global: { plugins: [i18n, pinia], stubs: { FontAwesomeIcon: true } },
  })
}

describe('ViewHeader', () => {
  beforeEach(() => {
    localStorage.clear()
    pinia = createPinia()
    setActivePinia(pinia)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    document.body.innerHTML = ''
  })

  it('shows the heading, caption and actions in its own row off a phone', () => {
    const wrapper = mountHeader({ title: 'Map', caption: '27 Aug - 25 Nov 2026' }, () =>
      h('button', { class: 'page-action' }, 'Filters'),
    )

    expect(wrapper.get('.view-header h1').text()).toBe('Map')
    expect(wrapper.get('.view-header .view-header__caption').text()).toBe('27 Aug - 25 Nov 2026')
    expect(wrapper.find('.view-header .view-header__actions .page-action').exists()).toBe(true)
  })

  it('offers no info button for a page without a line about it', () => {
    const wrapper = mountHeader({ title: 'Map' })

    expect(wrapper.find('.view-header__info').exists()).toBe(false)
  })

  // The cards' info buttons and the page's go together: "Show info icons" in Settings.
  it('leaves the info button out while info icons are switched off', async () => {
    const wrapper = mountHeader({ title: 'Trip log', subtitle: 'Every trip, for the taxman.' })
    expect(wrapper.find('.view-header__info').exists()).toBe(true)

    useUiSettingsStore(pinia).showCardInfoIcons = false
    await nextTick()

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

  it('moves the heading, caption and actions to the top bar on a phone, leaving the row empty', async () => {
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === PHONE_MEDIA_QUERY,
      media: query,
      addEventListener: () => {},
      removeEventListener: () => {},
    }))
    const title = document.body.appendChild(document.createElement('div'))
    title.id = 'mobile-topbar-title'
    const actions = document.body.appendChild(document.createElement('div'))
    actions.id = 'mobile-topbar-page-actions'

    const wrapper = mountHeader(
      { title: 'Trip log', caption: 'November 2026', subtitle: 'Every trip, for the taxman.' },
      () => h('button', { class: 'page-action' }, 'Filters'),
    )
    await flushPromises()

    expect(title.querySelector('h1')?.textContent).toBe('Trip log')
    expect(title.querySelector('.view-header__caption')?.textContent?.trim()).toBe('November 2026')
    expect(actions.querySelector('.view-header__actions .page-action')).not.toBeNull()
    // Nothing is left in the page's own row, which the phone stylesheet then hides.
    expect(wrapper.get('.view-header').element.children).toHaveLength(0)

    ;(title.querySelector('.view-header__info') as HTMLButtonElement).click()
    await flushPromises()
    expect(document.querySelector('[role="dialog"]')?.textContent).toContain(
      'Every trip, for the taxman.',
    )
  })
})
