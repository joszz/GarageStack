import { request, send } from '@/services/apiCore'

/** The sections an account's settings are kept in, one per settings store. Mirrors UserSettingsLimits.Sections. */
export const SETTINGS_SECTIONS = ['ui', 'dashboard', 'map'] as const
export type SettingsSection = (typeof SETTINGS_SECTIONS)[number]

/** Every section the account has saved, each a JSON object of its settings. One never saved is absent. */
export type AccountSettings = Partial<Record<SettingsSection, Record<string, unknown>>>

export const settingsApi = {
  load: () => request<AccountSettings>('/api/settings'),
  // Only the keys given change; the rest of the section keeps what the account holds. keepalive,
  // because the last change before a tab closes is sent while the page is going away.
  save: (section: SettingsSection, changes: Record<string, unknown>) =>
    send(`/api/settings/${section}`, 'PATCH', changes, { keepalive: true }),
}
