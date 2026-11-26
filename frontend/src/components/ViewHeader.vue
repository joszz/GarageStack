<script setup lang="ts">
// A page's title row: the heading, the page's actions on the right, and an optional caption under
// the heading inside the row (the period Statistics shows). A line about the page waits behind an
// info button beside the heading. Under it all, the notice that the server cannot be reached, so
// every page says so.
// A phone's top bar takes the whole row: the heading with its info button and caption on the left,
// the page's actions as icons on the right. The row itself is then left empty, and hidden.
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
    <Teleport defer to="#mobile-topbar-title" :disabled="!isPhone">
      <div class="view-header__heading">
        <div class="view-header__title">
          <h1>{{ title }}</h1>
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
        </div>
        <p v-if="caption" class="text-sm view-header__caption">
          <font-awesome-icon
            icon="calendar-days"
            class="view-header__caption-icon"
            aria-hidden="true"
          />
          <span>{{ caption }}</span>
        </p>
      </div>
    </Teleport>
    <Teleport v-if="$slots.actions" defer to="#mobile-topbar-page-actions" :disabled="!isPhone">
      <div class="view-header__actions">
        <slot name="actions" />
      </div>
    </Teleport>
  </div>
  <DetailModal v-if="subtitle" :open="aboutOpen" :title="title" @close="aboutOpen = false">
    <p class="card-info-desc">{{ subtitle }}</p>
  </DetailModal>
  <OfflineNotice />
</template>
