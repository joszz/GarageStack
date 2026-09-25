import { describe, it, expect, afterEach } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import MapLegend from '../MapLegend.vue'

// Stub FontAwesomeIcon so the test environment doesn't need the FA setup
const FaStub = { template: '<span />', props: ['icon'] }

const i18n = createI18n({
  legacy: false,
  locale: 'en',
  messages: { en: { trips: { legend: 'Legend' } } },
})

// Bound the way a parent's v-model:expanded would be, so a click round-trips through the prop.
// Attached to the document because isVisible() only tracks v-show reliably on a live tree.
function mountLegend(expanded: boolean) {
  const wrapper = mount(MapLegend, {
    props: {
      label: 'Speed limits',
      expanded,
      'onUpdate:expanded': (value: boolean) => wrapper.setProps({ expanded: value }),
    },
    slots: { default: '<p data-testid="key">Within the limit</p>' },
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: FaStub } },
    attachTo: document.body,
  })
  return wrapper
}

describe('MapLegend', () => {
  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('shows only its button while folded', () => {
    const wrapper = mountLegend(false)
    expect(wrapper.find('button').exists()).toBe(true)
    expect(wrapper.find('[data-testid="key"]').isVisible()).toBe(false)
    expect(wrapper.findComponent(FaStub).props('icon')).toBe('circle-info')
  })

  it('shows its contents while open, with a button that closes it', () => {
    const wrapper = mountLegend(true)
    expect(wrapper.find('[data-testid="key"]').isVisible()).toBe(true)
    expect(wrapper.findComponent(FaStub).props('icon')).toBe('xmark')
  })

  it('opens and folds again on the button', async () => {
    const wrapper = mountLegend(false)
    await wrapper.find('button').trigger('click')
    await flushPromises()
    expect(wrapper.emitted('update:expanded')?.[0]).toEqual([true])
    expect(wrapper.find('[data-testid="key"]').isVisible()).toBe(true)
    await wrapper.find('button').trigger('click')
    await flushPromises()
    expect(wrapper.emitted('update:expanded')?.[1]).toEqual([false])
    expect(wrapper.find('[data-testid="key"]').isVisible()).toBe(false)
  })

  it('tells assistive tech whether the key is open and which element it opens', async () => {
    const wrapper = mountLegend(false)
    const button = wrapper.find('button')
    const panel = wrapper.find('[role="group"]')
    expect(button.attributes('aria-expanded')).toBe('false')
    expect(button.attributes('aria-controls')).toBe(panel.attributes('id'))
    expect(button.attributes('aria-label')).toBe('Legend')
    await button.trigger('click')
    await flushPromises()
    expect(button.attributes('aria-expanded')).toBe('true')
  })

  it('names the key after what it explains', () => {
    const wrapper = mountLegend(true)
    expect(wrapper.find('[role="group"]').attributes('aria-label')).toBe('Speed limits')
  })
})
