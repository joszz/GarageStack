/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Build version from the git tag (via MinVer), injected by the Docker build. */
  readonly VITE_APP_VERSION?: string
  /**
   * MapLibre style documents for the dark and light themes, for deployments serving their own
   * vector tiles. Default to the public OpenFreeMap styles; a custom host must also be allowed
   * by connect-src and img-src in nginx-security-headers.conf.
   */
  readonly VITE_MAP_STYLE_DARK?: string
  readonly VITE_MAP_STYLE_LIGHT?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
