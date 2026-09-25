import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ToolbarPanel from '../ToolbarPanel.vue'

// Stub FontAwesomeIcon so the test environment doesn't need the FA setup
const FaStub = { template: '<span />', props: ['icon'] }

// Stub DetailModal with a minimal shim that exposes the open prop and a slot
const DetailModalStub = {
  name: 'DetailModal',
  template: '<div v-if="open" data-testid="modal"><slot /></div>',
  props: ['open', 'title'],
  emits: ['close'],
}

const i18n = createI18n({
  legacy: false,
  locale: 'en',
  messages: {
    en: { common: { filters: 'Filters', panelWithCount: '{label}, {n} active' } },
  },
})

function mountPanel(slotContent = '', props: Record<string, string | number> = {}) {
  return mount(ToolbarPanel, {
    props,
    global: {
      plugins: [i18n],
      stubs: {
        DetailModal: DetailModalStub,
        FontAwesomeIcon: FaStub,
      },
    },
    slots: slotContent ? { default: slotContent } : {},
  })
}

describe('ToolbarPanel', () => {
  it('renders the trigger button', () => {
    const wrapper = mountPanel()
    expect(wrapper.find('button').exists()).toBe(true)
  })

  it('modal is closed by default', () => {
    const wrapper = mountPanel()
    expect(wrapper.findComponent(DetailModalStub).props('open')).toBe(false)
  })

  it('opens the modal when the trigger button is clicked', async () => {
    const wrapper = mountPanel()
    await wrapper.find('button').trigger('click')
    expect(wrapper.findComponent(DetailModalStub).props('open')).toBe(true)
  })

  it('closes the modal when DetailModal emits close', async () => {
    const wrapper = mountPanel()
    await wrapper.find('button').trigger('click')
    expect(wrapper.findComponent(DetailModalStub).props('open')).toBe(true)
    await wrapper.findComponent(DetailModalStub).vm.$emit('close')
    expect(wrapper.findComponent(DetailModalStub).props('open')).toBe(false)
  })

  it('passes the filters label as the modal title', async () => {
    const wrapper = mountPanel()
    await wrapper.find('button').trigger('click')
    expect(wrapper.findComponent(DetailModalStub).props('title')).toBe('Filters')
  })

  it('defaults to the filters label and icon', () => {
    const wrapper = mountPanel()
    expect(wrapper.find('button').text()).toBe('Filters')
    expect(wrapper.findComponent(FaStub).props('icon')).toBe('sliders')
  })

  it('uses the given title and icon for the button and the modal', async () => {
    const wrapper = mountPanel('', { title: 'Layers', icon: 'layer-group' })
    expect(wrapper.find('button').text()).toBe('Layers')
    expect(wrapper.findComponent(FaStub).props('icon')).toBe('layer-group')
    await wrapper.find('button').trigger('click')
    expect(wrapper.findComponent(DetailModalStub).props('title')).toBe('Layers')
  })

  it('shows no count badge when nothing is active', () => {
    expect(mountPanel().find('.toolbar-panel__count').exists()).toBe(false)
    expect(mountPanel('', { count: 0 }).find('.toolbar-panel__count').exists()).toBe(false)
  })

  it('shows the active count in a badge on the button', () => {
    const wrapper = mountPanel('', { count: 3 })
    expect(wrapper.find('.toolbar-panel__count').text()).toBe('3')
  })

  it('names the button with its count so the badge is not read as a loose number', () => {
    expect(mountPanel('', { count: 2 }).find('button').attributes('aria-label')).toBe(
      'Filters, 2 active',
    )
    expect(mountPanel().find('button').attributes('aria-label')).toBe('Filters')
  })

  it('hides the badge from assistive tech, the button label carrying the count instead', () => {
    const badge = mountPanel('', { count: 1 }).find('.toolbar-panel__count')
    expect(badge.attributes('aria-hidden')).toBe('true')
  })

  it('renders slot content inside the modal', async () => {
    const wrapper = mountPanel('<p data-testid="slot-child">hello</p>')
    await wrapper.find('button').trigger('click')
    expect(wrapper.find('[data-testid="slot-child"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="slot-child"]').text()).toBe('hello')
  })

  it('slot content is not visible when the modal is closed', () => {
    const wrapper = mountPanel('<p data-testid="slot-child">hello</p>')
    // Modal starts closed; the stub hides its content via v-if
    expect(wrapper.find('[data-testid="slot-child"]').exists()).toBe(false)
  })
})
