import { describe, it, expect, beforeEach, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { createPinia, setActivePinia } from 'pinia'
import en from '@/locales/en.json'
import CommutePlacesModal from '../CommutePlacesModal.vue'
import { useCommutePlacesStore } from '@/stores/commutePlaces'
import type { CommutePlace, CommutePlaceChange } from '@/services/commutePlacesApi'

// The map itself is Leaflet's business; these stand-ins keep its props in reach.
vi.mock('@vue-leaflet/vue-leaflet', () => {
  const stub = (name: string, props: string[]) =>
    defineComponent({
      name,
      props,
      setup:
        (_, { slots }) =>
        () =>
          h('div', slots.default?.()),
    })
  return {
    LMap: stub('LMap', ['zoom', 'center']),
    LMarker: stub('LMarker', ['latLng', 'draggable']),
    LCircle: stub('LCircle', ['latLng', 'radius']),
  }
})
vi.mock('@/composables/useBasemap', () => ({ useBasemap: () => undefined }))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

const home: CommutePlace = {
  id: 1,
  kind: 'home',
  label: null,
  latitude: 52.0,
  longitude: 5.0,
  radiusMeters: 250,
}

function mountModal(props: Partial<{ pick: { latitude: number; longitude: number } | null }> = {}) {
  return mount(CommutePlacesModal, {
    props: { open: false, vin: 'VIN1', pick: null, fallbackCenter: null, ...props },
    global: {
      plugins: [i18n],
      stubs: { FontAwesomeIcon: true, Teleport: true, Transition: false },
    },
  })
}

async function open(wrapper: ReturnType<typeof mountModal>) {
  await wrapper.setProps({ open: true })
  await flushPromises()
}

function kindChip(wrapper: ReturnType<typeof mountModal>, label: string) {
  return wrapper.findAll('.toggle-chip').find((b) => b.text() === label)!
}

describe('CommutePlacesModal', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('opened from a trip end, starts a home at that end', async () => {
    const wrapper = mountModal({ pick: { latitude: 52.3, longitude: 4.9 } })
    await open(wrapper)

    expect(wrapper.findComponent({ name: 'LMarker' }).props('latLng')).toEqual([52.3, 4.9])
    expect(wrapper.find('[aria-pressed="true"]').text()).toBe('Home')
  })

  it('once home is set, a new place starts as work', async () => {
    useCommutePlacesStore().places = [home]
    const wrapper = mountModal({ pick: { latitude: 52.3, longitude: 4.9 } })
    await open(wrapper)

    expect(wrapper.find('[aria-pressed="true"]').text()).toBe('Work')
  })

  it('once home is set, offers no second home', async () => {
    useCommutePlacesStore().places = [home]
    const wrapper = mountModal()
    await open(wrapper)

    const buttons = wrapper.findAll('button').map((b) => b.text())
    expect(buttons).toContain('Add work')
    expect(buttons).not.toContain('Add home')

    await wrapper
      .findAll('button')
      .find((b) => b.text() === 'Add work')!
      .trigger('click')

    expect(kindChip(wrapper, 'Home').attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('This car already has a home.')
  })

  it('keeps home open to the place that is home', async () => {
    useCommutePlacesStore().places = [home]
    const wrapper = mountModal()
    await open(wrapper)

    await wrapper.find('.detail-list__item--link').trigger('click')

    expect(kindChip(wrapper, 'Home').attributes('disabled')).toBeUndefined()
    expect(wrapper.text()).not.toContain('This car already has a home.')
  })

  it('saves the place with a trimmed name and the radius, then says what changed', async () => {
    const store = useCommutePlacesStore()
    const change: CommutePlaceChange = { place: { ...home, id: 2 }, marked: 2, cleared: 0 }
    const save = vi.spyOn(store, 'savePlace').mockResolvedValue(change)
    const wrapper = mountModal({ pick: { latitude: 52.3, longitude: 4.9 } })
    await open(wrapper)

    await wrapper.find('#commute-place-label').setValue('  Home sweet home ')
    await wrapper
      .findAll('button')
      .find((b) => b.text() === 'Save')!
      .trigger('click')
    await flushPromises()

    expect(save).toHaveBeenCalledWith(
      'VIN1',
      { kind: 'home', label: 'Home sweet home', latitude: 52.3, longitude: 4.9, radiusMeters: 250 },
      undefined,
    )
    expect(wrapper.emitted('changed')).toEqual([[change]])
    expect(wrapper.find('[role="status"]').text()).toBe('2 trips marked as commute.')
  })

  it('removes a place only after it is confirmed', async () => {
    const store = useCommutePlacesStore()
    store.places = [home]
    const remove = vi
      .spyOn(store, 'removePlace')
      .mockResolvedValue({ place: null, marked: 0, cleared: 1 })
    const wrapper = mountModal()
    await open(wrapper)

    await wrapper.find('.detail-list__item--link').trigger('click')
    await wrapper
      .findAll('button')
      .find((b) => b.text() === 'Remove')!
      .trigger('click')
    expect(remove).not.toHaveBeenCalled()

    await wrapper
      .findAll('button')
      .find((b) => b.text() === 'Confirm')!
      .trigger('click')
    await flushPromises()

    expect(remove).toHaveBeenCalledWith('VIN1', 1)
    expect(wrapper.find('[role="status"]').text()).toBe('1 trip no longer marked as commute.')
  })
})
