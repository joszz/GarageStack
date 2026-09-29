import { describe, it, expect } from 'vitest'
import { useModal } from '@/composables/useModal'

describe('useModal', () => {
  it('starts closed', () => {
    const { isOpen } = useModal()
    expect(isOpen.value).toBe(false)
  })

  it('opens and closes', () => {
    const { isOpen, open, close } = useModal()
    open()
    expect(isOpen.value).toBe(true)
    close()
    expect(isOpen.value).toBe(false)
  })

  it('gives each caller its own state', () => {
    const a = useModal()
    const b = useModal()
    a.open()
    expect(b.isOpen.value).toBe(false)
  })
})
