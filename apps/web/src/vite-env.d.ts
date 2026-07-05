/// <reference types="vite/client" />

declare const __API_BASE__: string;

interface ImportMetaEnv {
  readonly VITE_API_BASE: string;
  readonly VITE_MAPBOX_TOKEN: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
