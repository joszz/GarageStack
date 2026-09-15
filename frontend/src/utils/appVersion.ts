/**
 * Formats the build version for display. The version comes from the git tag via MinVer and is
 * injected by the Docker build as VITE_APP_VERSION (see RELEASING.md). Returns null when no
 * version was injected, e.g. when running `pnpm dev`.
 */
export function formatAppVersion(version: string | undefined): string | null {
  const trimmed = version?.trim()
  return trimmed ? `v${trimmed}` : null
}

export const APP_VERSION = formatAppVersion(import.meta.env.VITE_APP_VERSION)
