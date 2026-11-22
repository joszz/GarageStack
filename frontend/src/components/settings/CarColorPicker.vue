<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  CAR_COLOR_SCHEMES,
  CUSTOM_CAR_COLOR_SCHEME,
  useUiSettingsStore,
  type CarColors,
} from '@/stores/settingsUi'
import { CAR_SILHOUETTE_MARKUP, CAR_SILHOUETTE_VIEWBOX } from '@/assets/carSilhouette'
import SettingsToggle from '../SettingsToggle.vue'

const { t } = useI18n()
const settings = useUiSettingsStore()

const customActive = computed(() => settings.carColorScheme === CUSTOM_CAR_COLOR_SCHEME)

// The custom swatch shows the user's own pair once there is one, and a rainbow until then.
const customSwatchStyle = computed(() =>
  settings.carCustomColors
    ? {
        '--swatch-p': settings.carCustomColors.primary,
        '--swatch-s': settings.carCustomColors.secondary,
      }
    : {},
)

const customFields: { key: keyof CarColors; label: string }[] = [
  { key: 'primary', label: 'settings.carColor.primary' },
  { key: 'secondary', label: 'settings.carColor.secondary' },
]

// Written on every move of the picker, so the car on the page follows it; the save waits for the
// burst to end. The pickers only show while custom is chosen, so the paints on screen are the
// custom pair.
function setCustomColor(key: keyof CarColors, event: Event) {
  const value = (event.target as HTMLInputElement).value
  settings.carCustomColors = { ...settings.carColors, [key]: value }
}
</script>

<template>
  <SettingsToggle
    class="settings-toggle--select"
    :label="t('settings.carColor.title')"
    :desc="t('settings.carColor.desc')"
  >
    <template #control>
      <div class="car-color-picker">
        <div class="car-color-swatches" role="group" :aria-label="t('settings.carColor.title')">
          <button
            v-for="scheme in CAR_COLOR_SCHEMES"
            :key="scheme.id"
            type="button"
            class="car-color-swatch"
            :class="{ 'car-color-swatch--active': settings.carColorScheme === scheme.id }"
            :title="t(`settings.carColor.${scheme.id}`)"
            :aria-label="t(`settings.carColor.${scheme.id}`)"
            :aria-pressed="settings.carColorScheme === scheme.id"
            :style="{ '--swatch-p': scheme.primary, '--swatch-s': scheme.secondary }"
            @click="settings.chooseCarColorScheme(scheme.id)"
          />
          <button
            type="button"
            class="car-color-swatch car-color-swatch--custom"
            :class="{
              'car-color-swatch--active': customActive,
              'car-color-swatch--unset': !settings.carCustomColors,
            }"
            :title="t('settings.carColor.custom')"
            :aria-label="t('settings.carColor.custom')"
            :aria-pressed="customActive"
            :style="customSwatchStyle"
            @click="settings.chooseCarColorScheme(CUSTOM_CAR_COLOR_SCHEME)"
          >
            <font-awesome-icon icon="palette" class="car-color-swatch__icon" />
          </button>
        </div>
        <div v-if="customActive" class="car-color-custom">
          <!-- The dialog hides the dashboard's car, so the picked paints show on this one. -->
          <svg
            class="car-color-preview"
            :viewBox="CAR_SILHOUETTE_VIEWBOX"
            aria-hidden="true"
            v-html="CAR_SILHOUETTE_MARKUP"
          />
          <div class="car-color-custom__fields">
            <label v-for="field in customFields" :key="field.key" class="car-color-custom__field">
              <input
                type="color"
                class="car-color-input"
                :value="settings.carColors[field.key]"
                @input="setCustomColor(field.key, $event)"
              />
              {{ t(field.label) }}
            </label>
          </div>
        </div>
      </div>
    </template>
  </SettingsToggle>
</template>
