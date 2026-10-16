import { describe, it, expect } from 'vitest'
import { nextTick, ref } from 'vue'
import { useCommandDraft } from '../useCommandDraft'

describe('useCommandDraft', () => {
  it('starts at what the car reports and follows it', async () => {
    const reported = ref(60)
    const draft = useCommandDraft(
      () => reported.value,
      () => false,
    )

    reported.value = 80
    await nextTick()

    expect(draft.value).toBe(80)
  })

  it('keeps the choice on its way to the car while the car still reports the old value', async () => {
    const reported = ref(60)
    const pending = ref(false)
    const draft = useCommandDraft(
      () => reported.value,
      () => pending.value,
    )

    draft.value = 90
    pending.value = true
    reported.value = 70
    await nextTick()

    expect(draft.value).toBe(90)
  })
})
