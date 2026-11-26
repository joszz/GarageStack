import { defineComponent, h } from 'vue'

/**
 * A stand-in for Teleport that renders its content in place, for a spec that wants a dialog
 * inside its wrapper. Use it rather than `teleport: true`: that stub is a new component on every
 * render, so whatever sits inside a Teleport (ViewHeader's actions, with their open filter
 * dialog) is rebuilt each time the page around it updates, and a spec ends up typing into a
 * dialog that is gone.
 */
export const InlineTeleport = defineComponent({
  name: 'InlineTeleport',
  setup:
    (_, { slots }) =>
    () =>
      h('div', { class: 'inline-teleport' }, slots.default?.()),
})
