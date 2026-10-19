<script setup lang="ts">
import {
  caretAfter,
  countPlateCharacters,
  formatPlate,
  LICENSE_PLATE_MAX_CHARACTERS,
  normalizePlate,
} from '@/utils/licensePlate'

// A Dutch licence plate to type on: yellow, with the blue EU band and NL on the left, and the
// dashes placed as the plate has them while typing (see formatPlate). The model is the plate as
// shown, dashes included; the API keeps it without them.
defineProps<{ label: string; placeholder?: string }>()
const model = defineModel<string>({ required: true })

// The EU band's ring of twelve upright stars, in a 24-unit box around its centre.
const STAR_POINTS = Array.from({ length: 10 }, (_, i) => {
  const radius = i % 2 === 0 ? 1.6 : 0.65
  const angle = (Math.PI / 5) * i - Math.PI / 2
  return `${(radius * Math.cos(angle)).toFixed(2)},${(radius * Math.sin(angle)).toFixed(2)}`
}).join(' ')

const STAR_POSITIONS = Array.from({ length: 12 }, (_, i) => {
  const angle = (Math.PI / 6) * i
  return `translate(${(8.5 * Math.sin(angle)).toFixed(2)} ${(-8.5 * Math.cos(angle)).toFixed(2)})`
})

function onInput(event: Event) {
  const input = event.target as HTMLInputElement
  let typed = input.value
  // The caret counted in letters and digits, since formatting moves the dashes around it.
  let before = countPlateCharacters(typed.slice(0, input.selectionStart ?? typed.length))

  // Backspace or Delete next to a dash would only take the dash, which formatting puts straight
  // back: take the letter or digit beyond it instead.
  const deleting = (event as InputEvent).inputType?.startsWith('delete') ?? false
  if (deleting && countPlateCharacters(typed) === countPlateCharacters(model.value)) {
    const characters = normalizePlate(typed)
    if ((event as InputEvent).inputType === 'deleteContentForward') {
      typed = characters.slice(0, before) + characters.slice(before + 1)
    } else if (before > 0) {
      typed = characters.slice(0, before - 1) + characters.slice(before)
      before -= 1
    }
  }

  const formatted = formatPlate(typed)
  model.value = formatted
  // Written back straight away: when formatting undoes the keystroke (a ninth character, a
  // stray symbol), the model does not change and Vue would leave the field as typed.
  input.value = formatted
  const caret = caretAfter(formatted, Math.min(before, LICENSE_PLATE_MAX_CHARACTERS))
  input.setSelectionRange(caret, caret)
}
</script>

<template>
  <span class="nl-plate">
    <span class="nl-plate__band" aria-hidden="true">
      <svg class="nl-plate__stars" viewBox="-12 -12 24 24" focusable="false">
        <polygon
          v-for="position in STAR_POSITIONS"
          :key="position"
          :points="STAR_POINTS"
          :transform="position"
        />
      </svg>
      <span class="nl-plate__country">NL</span>
    </span>
    <input
      class="nl-plate__input"
      :value="model"
      type="text"
      autocomplete="off"
      autocapitalize="characters"
      spellcheck="false"
      enterkeyhint="done"
      :placeholder="placeholder"
      :aria-label="label"
      @input="onInput"
    />
  </span>
</template>

<style scoped>
/* The colours of the plate itself, the same in either theme: reflective yellow with black
   characters, and the EU's blue and yellow on the band. */
.nl-plate {
  display: flex;
  flex: 1 1 auto;
  align-items: stretch;
  min-width: 0;
  max-width: 15rem;
  height: 2.75rem;
  overflow: hidden;
  background: #f5c400;
  border: 2px solid #111;
  border-radius: 6px;
  box-shadow:
    inset 0 0 0 1px rgb(255 255 255 / 40%),
    0 1px 3px rgb(0 0 0 / 30%);
}

.nl-plate:focus-within {
  outline: var(--focus-ring);
  outline-offset: 2px;
}

.nl-plate__band {
  display: flex;
  flex: 0 0 1.75rem;
  flex-direction: column;
  align-items: center;
  justify-content: space-between;
  padding: 0.2rem 0 0.15rem;
  color: #fff;
  background: #039;
}

.nl-plate__stars {
  width: 1.15rem;
  height: 1.15rem;
  fill: #fc0;
}

.nl-plate__country {
  font-family: Arial, Helvetica, sans-serif;
  font-size: 0.75rem;
  font-weight: 700;
  line-height: 1;
}

.nl-plate__input {
  flex: 1 1 auto;
  min-width: 0;
  padding: 0 0.5rem;
  border: 0;
  background: transparent;
  color: #111;
  font-family:
    Kenteken, 'DIN 1451 Mittelschrift', 'Arial Narrow', 'Roboto Condensed', Arial, sans-serif;
  font-size: 1.45rem;
  font-weight: 700;
  font-stretch: condensed;
  letter-spacing: 0.06em;
  text-align: center;
  text-transform: uppercase;
}

.nl-plate__input:focus {
  outline: none;
}

.nl-plate__input::placeholder {
  color: rgb(0 0 0 / 35%);
}
</style>
