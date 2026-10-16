/**
 * Stand-in for DetailModal in component specs: renders its body and footer while open, without
 * the Teleport and Transition that would move the content out of the mounted wrapper.
 */
export const DetailModalStub = {
  name: 'DetailModal',
  template: '<div v-if="open" data-testid="modal"><slot /><slot name="footer" /></div>',
  props: ['open', 'title', 'wide'],
  emits: ['close'],
}
