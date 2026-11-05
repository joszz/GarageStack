import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import StatusCard from '../StatusCard.vue'

function mountCard(props: Partial<InstanceType<typeof StatusCard>['$props']> = {}) {
  return mount(StatusCard, {
    props: { icon: 'road', label: 'Business', value: '42.5', unit: 'km', ...props },
    global: { stubs: { FontAwesomeIcon: true } },
  })
}

describe('StatusCard', () => {
  it('draws a share as a bar, named for screen readers', () => {
    const bar = mountCard({ share: 0.25, shareLabel: '25% of the distance' }).find('progress')

    expect((bar.element as HTMLProgressElement).value).toBe(0.25)
    expect(bar.attributes('max')).toBe('1')
    expect(bar.attributes('aria-label')).toBe('25% of the distance')
  })

  it('draws no bar without a share', () => {
    expect(mountCard().find('progress').exists()).toBe(false)
  })
})
