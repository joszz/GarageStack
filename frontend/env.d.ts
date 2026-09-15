/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Build version from the git tag (via MinVer), injected by the Docker build. */
  readonly VITE_APP_VERSION?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
