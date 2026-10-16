import { ref, watch, type Ref } from 'vue'

/**
 * A control's draft of what a command will set on the car. It starts at what the car reports and
 * follows it, except while the change it sent is still on its way: a car that answers slowly
 * should not snap the control back to the old value before the new one has arrived.
 *
 * @param current What the car reports now; read again whenever its sources change.
 * @param isPending Whether this control's command is waiting for the car.
 */
export function useCommandDraft<T>(current: () => T, isPending: () => boolean): Ref<T> {
  const draft = ref(current()) as Ref<T>
  watch(current, (now) => {
    if (!isPending()) draft.value = now
  })
  return draft
}
