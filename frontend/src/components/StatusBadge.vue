<script setup lang="ts">
import type { BadgeVariant } from '@/utils/levels'

// A status as a small coloured label, in one of the badge variants main.css draws.
defineProps<{ variant: BadgeVariant }>()
</script>

<template>
  <span class="badge" :class="`badge-${variant}`"><slot /></span>
</template>
