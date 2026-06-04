import { describe, it, expect } from 'vitest'
import { useModal } from '@/composables/useModal'

describe('useModal', () => {
  it('starts closed by default', () => {
    const { isOpen } = useModal()
    expect(isOpen.value).toBe(false)
  })

  it('starts open when initialOpen is true', () => {
    const { isOpen } = useModal(true)
    expect(isOpen.value).toBe(true)
  })

  it('open() sets isOpen to true', () => {
    const { isOpen, open } = useModal()
    open()
    expect(isOpen.value).toBe(true)
  })

  it('open() is idempotent when already open', () => {
    const { isOpen, open } = useModal(true)
    open()
    expect(isOpen.value).toBe(true)
  })

  it('close() sets isOpen to false', () => {
    const { isOpen, close } = useModal(true)
    close()
    expect(isOpen.value).toBe(false)
  })

  it('close() is idempotent when already closed', () => {
    const { isOpen, close } = useModal()
    close()
    expect(isOpen.value).toBe(false)
  })

  it('toggle() opens a closed modal', () => {
    const { isOpen, toggle } = useModal()
    toggle()
    expect(isOpen.value).toBe(true)
  })

  it('toggle() closes an open modal', () => {
    const { isOpen, toggle } = useModal(true)
    toggle()
    expect(isOpen.value).toBe(false)
  })

  it('toggle() alternates state on successive calls', () => {
    const { isOpen, toggle } = useModal()
    toggle()
    toggle()
    toggle()
    expect(isOpen.value).toBe(true)
  })

  it('each call to useModal returns independent state', () => {
    const a = useModal()
    const b = useModal()
    a.open()
    expect(b.isOpen.value).toBe(false)
  })
})
