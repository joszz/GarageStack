<script setup lang="ts">
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
</script>

<template>
  <div class="demo-banner" role="status">
    <font-awesome-icon :icon="['fas', 'triangle-exclamation']" />
    {{ t('demo.banner') }}
  </div>
</template>
