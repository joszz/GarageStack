/**
 * Formats the build version for display. The version comes from the git tag via MinVer and is
 * injected by the Docker build as VITE_APP_VERSION (see RELEASING.md). A leading "v" is accepted
 * so a tag name such as "v0.5.0" can be passed as is. Returns null when no version was injected,
 * e.g. when running `pnpm dev`.
 */
export function formatAppVersion(version: string | undefined): string | null {
  const bare = version?.trim().replace(/^v/i, '')
  return bare ? `v${bare}` : null
}

export const APP_VERSION = formatAppVersion(import.meta.env.VITE_APP_VERSION)
