<script setup lang="ts">
// A page's title row: the heading, the page's actions on the right, and an optional caption under
// the heading inside the row (the period Statistics shows). A line about the page waits behind an
// info button beside the heading. Under it all, the notice that the server cannot be reached, so
// every page says so.
// A phone's top bar shows the heading in place of the app's name, which leaves the page's own row
// to the caption and the actions.
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import DetailModal from '@/components/DetailModal.vue'
import OfflineNotice from '@/components/OfflineNotice.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import { PHONE_MEDIA_QUERY } from '@/utils/viewport'

defineProps<{
  title: string
  caption?: string
  subtitle?: string
}>()

const { t } = useI18n()
const isPhone = useMediaQuery(PHONE_MEDIA_QUERY)
const aboutOpen = ref(false)
</script>

<template>
  <div class="view-header">
    <div v-if="caption" class="view-header__heading">
      <Teleport defer to="#mobile-topbar-title" :disabled="!isPhone">
        <h1>{{ title }}</h1>
      </Teleport>
      <p class="text-muted text-sm view-header__caption">{{ caption }}</p>
    </div>
    <Teleport v-else defer to="#mobile-topbar-title" :disabled="!isPhone">
      <h1>{{ title }}</h1>
    </Teleport>
    <button
      v-if="subtitle"
      type="button"
      class="view-header__info"
      aria-haspopup="dialog"
      :aria-label="t('common.aboutPage')"
      :title="t('common.aboutPage')"
      @click="aboutOpen = true"
    >
      <font-awesome-icon icon="circle-info" />
    </button>
    <div v-if="$slots.actions" class="view-header__actions">
      <slot name="actions" />
    </div>
  </div>
  <DetailModal v-if="subtitle" :open="aboutOpen" :title="title" @close="aboutOpen = false">
    <p class="card-info-desc">{{ subtitle }}</p>
  </DetailModal>
  <OfflineNotice />
</template>
