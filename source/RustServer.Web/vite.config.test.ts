import type { ConfigEnv, ProxyOptions, UserConfig } from 'vite'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'

import viteConfig from './vite.config.js'

const topology = {
  RUSTSERVER_CONTROL_URL: 'http://127.0.0.1:5112/',
  RUSTSERVER_VERSIONS_URL: 'http://127.0.0.1:5113/',
  RUSTSERVER_PLUGINS_URL: 'http://127.0.0.1:5114/',
  RUSTSERVER_SNAPSHOTS_URL: 'http://127.0.0.1:5115/',
  RUSTSERVER_PLUGIN_CATALOG_URL: 'http://127.0.0.1:5116/',
  RUSTSERVER_WEB_URL: 'http://127.0.0.1:5173/',
  RUSTSERVER_WEB_BIND_HOST: '127.0.0.1',
} as const

const topologyNames = Object.keys(topology) as Array<keyof typeof topology>
const priorTopology: Partial<Record<keyof typeof topology, string | undefined>> = {}

beforeEach(() => {
  for (const name of topologyNames) {
    priorTopology[name] = process.env[name]
    process.env[name] = topology[name]
  }
})

afterEach(() => {
  for (const name of topologyNames) {
    const priorValue = priorTopology[name]

    if (priorValue === undefined) {
      delete process.env[name]
    } else {
      process.env[name] = priorValue
    }
  }
})

async function developmentConfiguration(): Promise<UserConfig> {
  if (typeof viteConfig !== 'function') {
    return viteConfig
  }

  const environment: ConfigEnv = {
    command: 'serve',
    mode: 'development',
    isSsrBuild: false,
    isPreview: false,
  }

  return await viteConfig(environment)
}

function proxyFor(configuration: UserConfig, prefix: string): ProxyOptions {
  const proxy = configuration.server?.proxy
  const proxyConfiguration = proxy?.[prefix]

  if (proxyConfiguration === undefined || typeof proxyConfiguration === 'string') {
    throw new Error(`Expected an object proxy configuration for '${prefix}'.`)
  }

  return proxyConfiguration
}

describe('Vite development routing', () => {
  it('serves static resources below the dashboard base path', async () => {
    const configuration = await developmentConfiguration()

    expect(configuration.base).toBe('/dashboard/')
  })

  it.each([
    ['/control-api', topology.RUSTSERVER_CONTROL_URL],
    ['/versions-api', topology.RUSTSERVER_VERSIONS_URL],
    ['/plugins-api', topology.RUSTSERVER_PLUGINS_URL],
    ['/snapshots-api', topology.RUSTSERVER_SNAPSHOTS_URL],
    ['/plugin-catalog-api', topology.RUSTSERVER_PLUGIN_CATALOG_URL],
  ])('keeps %s as an origin-root proxy route', async (prefix, target) => {
    const configuration = await developmentConfiguration()
    const proxy = proxyFor(configuration, prefix)

    expect(proxy.target).toBe(target.slice(0, -1))
    expect(proxy.rewrite?.(`${prefix}/api/health`)).toBe('/api/health')
  })
})
