import react from '@vitejs/plugin-react';
import { configDefaults, defineConfig } from 'vitest/config';

function requireTopologyUrl(name: string): URL {
  const value = process.env[name]?.trim()

  if (value === undefined || value === '') {
    throw new Error(`Configuration value '${name}' is required.`)
  }

  let url: URL
  try {
    url = new URL(value)
  } catch {
    throw new Error(`Configuration value '${name}' must be an absolute HTTP or HTTPS URL.`)
  }

  if (
    (url.protocol !== 'http:' && url.protocol !== 'https:') ||
    url.username !== '' ||
    url.password !== '' ||
    url.pathname !== '/' ||
    url.search !== '' ||
    url.hash !== '' ||
    url.port === ''
  ) {
    throw new Error(
      `Configuration value '${name}' must be an absolute HTTP or HTTPS URL with an explicit port and without credentials, a path, query, or fragment.`,
    )
  }

  return url
}

function requireIpv4BindHost(name: string): string {
  const value = process.env[name]?.trim()

  if (value === undefined || value === '') {
    throw new Error(`Configuration value '${name}' is required.`)
  }

  const octets = value.split('.')
  if (
    octets.length !== 4 ||
    octets.some((octet) =>
      !/^(0|[1-9]\d{0,2})$/.test(octet) || Number.parseInt(octet, 10) > 255,
    )
  ) {
    throw new Error(`Configuration value '${name}' must be a canonical IPv4 bind host.`)
  }

  return value
}

function proxy(target: URL, prefix: string) {
  return {
    target: target.origin,
    changeOrigin: true,
    rewrite: (path: string) => path.replace(new RegExp(`^${prefix}(?=/|$)`), '')
  }
}

export default defineConfig(({ command, mode }) => {
  const productionApiBaseUrls = {
    'import.meta.env.VITE_CONTROL_API_BASE_URL': JSON.stringify('/control-api'),
    'import.meta.env.VITE_VERSIONS_API_BASE_URL': JSON.stringify('/versions-api'),
    'import.meta.env.VITE_PLUGINS_API_BASE_URL': JSON.stringify('/plugins-api'),
    'import.meta.env.VITE_PLUGIN_CATALOG_API_BASE_URL': JSON.stringify('/plugin-catalog-api'),
    'import.meta.env.VITE_SNAPSHOTS_API_BASE_URL': JSON.stringify('/snapshots-api'),
  }

  const sharedConfiguration = {
    base: '/dashboard/',
    plugins: [react()],
    test: {
      environment: 'jsdom',
      setupFiles: './src/test/setup.ts',
      exclude: [...configDefaults.exclude, '**/old/**'],
    },
  }

  // The topology controls only the Vite development server. Production is a
  // static same-origin application served by the platform Nginx configuration,
  // so builds must not need a VM address, bind host, or backend port.
  if (command !== 'serve' || mode === 'test') {
    return {
      ...sharedConfiguration,
      define: productionApiBaseUrls,
    }
  }

  const controlUrl = requireTopologyUrl('RUSTSERVER_CONTROL_URL')
  const versionsUrl = requireTopologyUrl('RUSTSERVER_VERSIONS_URL')
  const pluginsUrl = requireTopologyUrl('RUSTSERVER_PLUGINS_URL')
  const snapshotsUrl = requireTopologyUrl('RUSTSERVER_SNAPSHOTS_URL')
  const pluginCatalogUrl = requireTopologyUrl('RUSTSERVER_PLUGIN_CATALOG_URL')
  const webUrl = requireTopologyUrl('RUSTSERVER_WEB_URL')
  const webBindHost = requireIpv4BindHost('RUSTSERVER_WEB_BIND_HOST')

  return {
    ...sharedConfiguration,
    server: {
      host: webBindHost,
      port: Number.parseInt(webUrl.port, 10),
      strictPort: true,
      proxy: {
        '/control-api': proxy(controlUrl, '/control-api'),
        '/versions-api': proxy(versionsUrl, '/versions-api'),
        '/plugins-api': proxy(pluginsUrl, '/plugins-api'),
        '/snapshots-api': proxy(snapshotsUrl, '/snapshots-api'),
        '/plugin-catalog-api': proxy(pluginCatalogUrl, '/plugin-catalog-api')
      }
    }
  }
});
