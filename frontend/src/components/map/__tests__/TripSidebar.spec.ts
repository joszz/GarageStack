import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import TripSidebar from '../TripSidebar.vue'
import type { TripRow } from '@/utils/tripRows'

const i18n = createI18n({ legacy: false, locale: 'en', missingWarn: false, fallbackWarn: false })

function row(from: string): TripRow {
  return {
    mode: 'route',
    from,
    to: 'Utrecht',
    routeTitle: `${from} to Utrecht`,
    dateLabel: 'Sep 26',
    timeLabel: '10:00',
    meta: '12 km',
    metaTitle: '12 km',
  }
}

function mountSidebar(props: Partial<InstanceType<typeof TripSidebar>['$props']> = {}) {
  return mount(TripSidebar, {
    props: {
      rows: [
        { realIdx: 1, row: row('Zwolle') },
        { realIdx: 0, row: row('Deventer') },
      ],
      loading: false,
      empty: false,
      selectedIndex: null,
      activeIndex: null,
      skeletonRows: 3,
      ...props,
    },
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true } },
  })
}

describe('TripSidebar', () => {
  it('asks for the trip behind a row by its index in the store', async () => {
    const wrapper = mountSidebar()

    await wrapper.findAll('.trip-list__item')[1]!.trigger('click')

    expect(wrapper.emitted('select')).toEqual([[0]])
  })

  it('marks the selected trip and the one being driven', () => {
    const wrapper = mountSidebar({ selectedIndex: 1, activeIndex: 0 })
    const items = wrapper.findAll('.trip-list__item')

    expect(items[0]!.classes()).toContain('trip-list__item--active')
    expect(items[1]!.find('.trip-list__live-badge').exists()).toBe(true)
  })

  it('shows placeholders while loading', () => {
    const wrapper = mountSidebar({ loading: true })

    expect(wrapper.findAll('.trip-list__item')).toHaveLength(3)
    expect(wrapper.find('.trip-list__place').exists()).toBe(false)
  })

  it('exposes its scrolling list and the end marker that grows it', () => {
    const wrapper = mountSidebar()
    const exposed = wrapper.vm as unknown as { root: unknown; sentinel: unknown }

    expect(exposed.root).toBeInstanceOf(HTMLElement)
    expect(exposed.sentinel).toBeInstanceOf(HTMLElement)
  })
})
