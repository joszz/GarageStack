<script setup lang="ts">
defineProps<{
  icon?: string
}>()
</script>

<template>
  <div class="status-card">
    <div class="status-card__icon">
      <font-awesome-icon v-if="icon" :icon="icon" />
      <span v-else class="skeleton skeleton--icon"></span>
    </div>
    <div class="status-card__body">
      <span class="skeleton skeleton--text-lg card-skel__label">&nbsp;</span>
      <span class="skeleton skeleton--text-sm card-skel__value">&nbsp;</span>
    </div>
  </div>
</template>

<style scoped>
/* Body collapses without content — make it fill the remaining card width */
.status-card__body {
  flex: 1;
  min-width: 0;
}

/*
 * Use the same font-size as the real elements so the browser computes
 * the line-box height from actual font metrics — no manual calc needed.
 * The &nbsp; provides the content that drives that height; color:transparent
 * hides it behind the shimmer gradient.
 */
.card-skel__label {
  font-size: 0.72rem;
  color: transparent;
}
.card-skel__value {
  font-size: 1.05rem;
  color: transparent;
}
</style>
