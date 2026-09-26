<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { CAR_COLOR_SCHEMES, useUiSettingsStore } from '@/stores/settingsUi'

const { t } = useI18n()
const settings = useUiSettingsStore()
</script>

<template>
  <div class="car-color-picker">
    <span class="settings-toggle__label">{{
      t(`settings.carColor.${settings.carColorScheme}`)
    }}</span>
    <div class="car-color-swatches">
      <button
        v-for="scheme in CAR_COLOR_SCHEMES"
        :key="scheme.id"
        class="car-color-swatch"
        :class="{ 'car-color-swatch--active': settings.carColorScheme === scheme.id }"
        :title="t(`settings.carColor.${scheme.id}`)"
        :aria-label="t(`settings.carColor.${scheme.id}`)"
        :style="{ '--swatch-p': scheme.primary, '--swatch-s': scheme.secondary }"
        @click="settings.carColorScheme = scheme.id"
      />
    </div>
  </div>
</template>
