export interface FrontendEnvironment {
  controlApiBaseUrl: string
  versionsApiBaseUrl: string
  pluginsApiBaseUrl: string
  pluginCatalogApiBaseUrl: string
  snapshotsApiBaseUrl: string
}

function readEnvironmentValue(value: string | undefined, fallback: string): string {
  // Production is intentionally fixed to the platform-owned, same-origin
  // Nginx routes. This prevents an inherited VITE_* shell variable from
  // embedding a development topology or backend address in a release asset.
  if (import.meta.env.PROD) {
    return fallback
  }

  const normalizedValue = value?.trim()

  return normalizedValue === '' || normalizedValue === undefined
    ? fallback
    : normalizedValue
}

export const environment: Readonly<FrontendEnvironment> = {
  controlApiBaseUrl: readEnvironmentValue(
    import.meta.env.VITE_CONTROL_API_BASE_URL,
    '/control-api',
  ),
  versionsApiBaseUrl: readEnvironmentValue(
    import.meta.env.VITE_VERSIONS_API_BASE_URL,
    '/versions-api',
  ),
  pluginsApiBaseUrl: readEnvironmentValue(
    import.meta.env.VITE_PLUGINS_API_BASE_URL,
    '/plugins-api',
  ),
  pluginCatalogApiBaseUrl: readEnvironmentValue(
    import.meta.env.VITE_PLUGIN_CATALOG_API_BASE_URL,
    '/plugin-catalog-api',
  ),
  snapshotsApiBaseUrl: readEnvironmentValue(
    import.meta.env.VITE_SNAPSHOTS_API_BASE_URL,
    '/snapshots-api',
  ),
}
