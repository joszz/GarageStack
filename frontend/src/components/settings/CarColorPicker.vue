<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { CAR_COLOR_SCHEMES, useUiSettingsStore } from '@/stores/settingsUi'
import SettingsToggle from '../SettingsToggle.vue'

const { t } = useI18n()
const settings = useUiSettingsStore()
</script>

<template>
  <SettingsToggle
    class="settings-toggle--select"
    :label="t('settings.carColor.title')"
    :desc="t('settings.carColor.desc')"
  >
    <template #control>
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
          @click="settings.carColorScheme = scheme.id"
        />
      </div>
    </template>
  </SettingsToggle>
</template>
