import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import FiltersPanel from '../FiltersPanel.vue'
import DetailModal from '../DetailModal.vue'

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
  messages: { en: { common: { filters: 'Filters' } } },
})

function mountPanel(slotContent = '') {
  return mount(FiltersPanel, {
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

describe('FiltersPanel', () => {
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
