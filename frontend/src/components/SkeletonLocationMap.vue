<template>
  <div class="location-map-card">
    <div class="map-skel__title skeleton skeleton--text skeleton--text-sm"></div>
    <div class="map-skel__map skeleton"></div>
  </div>
</template>

<style scoped>
.map-skel__title {
  height: 0.85rem;
  margin-bottom: 1rem;
  max-width: 5rem;
}
.map-skel__map {
  flex: 1;
  min-height: 180px;
  border-radius: var(--radius);
}
</style>
