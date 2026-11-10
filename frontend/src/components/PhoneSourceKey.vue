<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
</script>

<template>
  <span class="phone-source-key text-xs"
    ><span class="phone-source-key__swatch" aria-hidden="true" />{{
      t('dawarich.phonePoints')
    }}</span
  >
</template>

<!-- Every map imports this key, including the trip log without the full map stylesheet. -->
<style>
.trip-phone-line {
  stroke: #c084fc;
}

.phone-source-key {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
}

.phone-source-key__swatch {
  width: 1.5rem;
  border-top: 3px dotted #c084fc;
}
</style>
