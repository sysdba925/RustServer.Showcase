import { fireEvent, render, screen } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { dashboardBasename, routes } from './router/router'

const api = vi.hoisted(() => ({
  HttpError: class HttpError extends Error {
    status: number
    problemDetails: { title?: string | null; detail?: string | null } | undefined

    constructor(status: number, detail?: string) {
      super(`HTTP ${status}`)
      this.status = status
      this.problemDetails = detail ? { detail } : undefined
    }
  },
  getNetworkAccess: vi.fn(),
  getServerConfiguration: vi.fn(),
  getServerStatus: vi.fn(),
  getStartupSchema: vi.fn(),
  getStartupSnapshot: vi.fn(),
  getRustVersions: vi.fn(),
  getOxideVersions: vi.fn(),
  updateServerConfiguration: vi.fn(),
  updateNetworkAccess: vi.fn(),
  requestWipe: vi.fn(),
  getSnapshots: vi.fn(),
  createSnapshot: vi.fn(),
  restoreSnapshot: vi.fn(),
  deleteSnapshot: vi.fn(),
  getSnapshotOperation: vi.fn(),
  getPluginsEnvironment: vi.fn(),
  getPluginsBaseline: vi.fn(),
  getPluginsGitStatus: vi.fn(),
  getPluginsRuntimeDiff: vi.fn(),
  getPluginsRuntimeDiffContent: vi.fn(),
  getPluginsReleases: vi.fn(),
  updatePluginsEnvironment: vi.fn(),
  fetchPluginsGit: vi.fn(),
  syncPluginsGit: vi.fn(),
  previewPluginsSource: vi.fn(),
  applyPluginsSource: vi.fn(),
  commitPluginsRuntime: vi.fn(),
  revertPluginsRuntime: vi.fn(),
  revertAllPluginsRuntime: vi.fn(),
  getPluginsOperations: vi.fn(),
  getPluginsOperation: vi.fn(),
  pluginSourceKinds: ['Website', 'GitHub', 'Codefling', 'UMod', 'Documentation', 'VersionLookup'],
  localPluginMediaKinds: ['Icon', 'Screenshot', 'AnimatedImage', 'Preview'],
  externalPluginMediaKinds: ['Video', 'Preview'],
  resolvePluginCatalogContentUrl: vi.fn((path: string) => `/plugin-catalog-api${path}`),
  getPluginCatalogPlugins: vi.fn(),
  getPluginCatalogSites: vi.fn(),
  getPluginCatalogTags: vi.fn(),
  getPluginCatalogFolders: vi.fn(),
  assignPluginCatalogTag: vi.fn(),
  removePluginCatalogTag: vi.fn(),
  assignPluginCatalogFolder: vi.fn(),
  removePluginCatalogFolder: vi.fn(),
  createPluginCatalogPlugin: vi.fn(),
  getPluginCatalogPlugin: vi.fn(),
  updatePluginCatalogPlugin: vi.fn(),
  deletePluginCatalogPlugin: vi.fn(),
  getPluginCatalogSources: vi.fn(),
  createPluginCatalogSource: vi.fn(),
  updatePluginCatalogSource: vi.fn(),
  deletePluginCatalogSource: vi.fn(),
  getPluginCatalogMedia: vi.fn(),
  uploadPluginCatalogMedia: vi.fn(),
  createExternalPluginCatalogMedia: vi.fn(),
  updatePluginCatalogMedia: vi.fn(),
  deletePluginCatalogMedia: vi.fn(),
  updatePluginCatalogIcon: vi.fn(),
}))

vi.mock('../shared/api', () => api)

beforeEach(() => {
  vi.clearAllMocks()
  api.getPluginCatalogSites.mockResolvedValue([])
  api.getPluginCatalogTags.mockResolvedValue([])
  api.getPluginCatalogFolders.mockResolvedValue([])
  api.getNetworkAccess.mockResolvedValue({ publicEnabled: true, maintenancePort: 50015 })
  api.getServerConfiguration.mockResolvedValue({
    desired: {
      hostname: 'Test server',
      description: 'Test description',
      maxPlayers: 50,
      tickRate: 30,
      saveInterval: 300,
      seed: 12345,
      worldSize: 4200,
    },
    effective: null,
  })
  api.getServerStatus.mockResolvedValue({ isRunning: true, isReady: true })
  api.getStartupSchema.mockResolvedValue({
    version: 1,
    milestones: [{ id: 'engine', label: 'Unity Engine' }],
  })
  api.getStartupSnapshot.mockResolvedValue({
    sessionId: null,
    started: false,
    milestones: [],
  })
  api.getRustVersions.mockResolvedValue({
    branch: 'public',
    installedBuildId: 12345678,
    running: null,
    availableBuildId: 12345678,
    updateAvailable: false,
  })
  api.getOxideVersions.mockResolvedValue({
    installedVersion: '2.0.7726',
    runningVersion: null,
    availableVersion: '2.0.7726',
    updateAvailable: false,
  })
  api.getSnapshots.mockResolvedValue([])
  api.getPluginsEnvironment.mockResolvedValue({ environment: 'staging', systemState: 'ready' })
  api.getPluginsBaseline.mockResolvedValue({
    status: 'ready',
    baseline: {
      environment: 'staging',
      source: 'development',
      release: null,
      pluginsCommit: 'abc123',
      configsCommit: 'def456',
      appliedAt: '2026-10-08T10:00:00Z',
    },
  })
  api.getPluginsGitStatus.mockResolvedValue({
    gitStatus: 'ready',
    lastFetchAt: null,
    plugins: { configured: true, localBranch: 'develop', localHead: 'abc123', remoteBranch: 'origin/develop', remoteHead: 'abc123', workspace: 'clean', relation: 'equal', diagnostic: null },
    configs: { configured: true, localBranch: 'develop', localHead: 'def456', remoteBranch: 'origin/develop', remoteHead: 'def456', workspace: 'clean', relation: 'equal', diagnostic: null },
  })
  api.getPluginsRuntimeDiff.mockResolvedValue({ status: 'ready', changes: [] })
  api.getPluginsReleases.mockResolvedValue({ releases: [] })
  api.getPluginsOperations.mockResolvedValue({ items: [], nextCursor: null })
  api.getPluginCatalogPlugins.mockResolvedValue({
    items: [],
    page: 1,
    pageSize: 12,
    totalItems: 0,
    totalPages: 0,
  })
})

function renderRoute(path: string) {
  const router = createMemoryRouter(routes, {
    basename: dashboardBasename,
    initialEntries: [path],
  })

  return render(<RouterProvider router={router} />)
}

describe('App', () => {
  it('renders the Overview route within the application shell', () => {
    renderRoute('/dashboard')

    expect(screen.getByRole('heading', { name: 'Overview' })).toBeVisible()
    expect(screen.getByText('RUSTSERVER')).toBeVisible()
    expect(screen.getByText('/ Control Panel')).toBeVisible()
    expect(screen.getByRole('link', { current: 'page', name: 'Overview' })).toBeVisible()
    expect(screen.getByRole('link', { name: 'Server' })).toBeVisible()
    expect(screen.getByRole('link', { name: 'World' })).toBeVisible()
    expect(screen.getByRole('link', { name: 'Plugins' })).toBeVisible()
  })

  it('uses the official Rust icon in the shared header with an R fallback', () => {
    renderRoute('/dashboard')

    const logo = screen.getByRole('img', { name: 'Rust game logo' })
    expect(logo).toHaveAttribute('src', 'https://rust.facepunch.com/favicon.ico')

    fireEvent.error(logo)

    expect(screen.getByRole('img', { name: 'Rust game logo unavailable' })).toHaveTextContent('R')
    expect(screen.queryByRole('img', { name: 'Rust game logo' })).not.toBeInTheDocument()
  })

  it('generates dashboard-prefixed URLs for all primary navigation routes', () => {
    renderRoute('/dashboard/')

    expect(screen.getByRole('link', { name: 'Overview' })).toHaveAttribute(
      'href',
      '/dashboard',
    )
    expect(screen.getByRole('link', { name: 'Server' })).toHaveAttribute(
      'href',
      '/dashboard/server',
    )
    expect(screen.getByRole('link', { name: 'World' })).toHaveAttribute(
      'href',
      '/dashboard/world',
    )
    expect(screen.getByRole('link', { name: 'Plugins' })).toHaveAttribute(
      'href',
      '/dashboard/plugins',
    )
  })

  it('renders the Overview structural regions', () => {
    renderRoute('/dashboard/')

    expect(screen.getByRole('region', { name: 'Server' })).toBeVisible()
    expect(screen.getByRole('region', { name: 'Software' })).toBeVisible()
    expect(screen.getByRole('region', { name: 'Activity' })).toBeVisible()
    expect(screen.getByRole('link', { name: 'Manage server' })).toHaveAttribute(
      'href',
      '/dashboard/server',
    )
    expect(screen.queryByRole('button', { name: 'Start' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Stop' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Restart' })).not.toBeInTheDocument()
  })

  it('renders the Server route and marks only Server as active', async () => {
    renderRoute('/dashboard/server')

    expect(screen.getByRole('heading', { name: 'Server' })).toBeVisible()
    expect(screen.getByRole('link', { current: 'page', name: 'Server' })).toBeVisible()
    expect(screen.getByRole('link', { name: 'Overview' })).not.toHaveAttribute(
      'aria-current',
      'page',
    )
    expect(await screen.findByRole('button', { name: 'Stop' })).toBeEnabled()
  })

  it('renders World management and marks World as active', async () => {
    renderRoute('/dashboard/world')

    expect(screen.getByRole('heading', { name: 'World' })).toBeVisible()
    expect(await screen.findByRole('heading', { name: 'Next World' })).toBeVisible()
    expect(screen.getByRole('article', { name: 'Next world configuration' })).toBeVisible()
    expect(screen.getByRole('article', { name: 'Wipe plan' })).toBeVisible()
    expect(screen.getByRole('article', { name: 'Snapshots' })).toBeVisible()
    expect(await screen.findByText('No snapshots yet.')).toBeVisible()
    expect(screen.getByRole('button', { name: 'Execute wipe' })).toBeDisabled()
    expect(screen.getByRole('link', { current: 'page', name: 'World' })).toBeVisible()
  })

  it('renders Plugins management and marks Plugins as active', async () => {
    renderRoute('/dashboard/plugins')

    expect(screen.getByRole('heading', { name: 'Plugins' })).toBeVisible()
    expect(await screen.findByRole('heading', { name: 'Git Repositories' })).toBeVisible()
    expect(screen.getByRole('heading', { name: 'Applied Baseline' })).toBeVisible()
    expect(screen.getByRole('button', { name: /Apply Source/ })).toBeEnabled()
    expect(screen.getByRole('link', { current: 'page', name: 'Plugins' })).toBeVisible()
    expect(screen.getByRole('link', { name: 'Overview' })).not.toHaveAttribute(
      'aria-current',
      'page',
    )
  })

  it('renders the Catalog within Plugins and keeps the primary Plugins navigation active', async () => {
    renderRoute('/dashboard/plugins/catalog')

    expect(screen.getByRole('heading', { name: 'Plugin catalog' })).toBeVisible()
    expect(await screen.findByText('No plugins match these filters.')).toBeVisible()
    expect(screen.getByRole('link', { current: 'page', name: 'Plugins' })).toBeVisible()
    expect(screen.getByRole('link', { current: 'page', name: 'Catalog' })).toBeVisible()
  })

  it('navigates to Plugins from the shared sidebar', () => {
    renderRoute('/dashboard/')

    fireEvent.click(screen.getByRole('link', { name: 'Plugins' }))

    expect(screen.getByRole('heading', { name: 'Plugins' })).toBeVisible()
    expect(screen.getByRole('link', { current: 'page', name: 'Plugins' })).toBeVisible()
  })

  it('shares a saved desired configuration from Server with World', async () => {
    const savedDesiredConfiguration = {
      hostname: 'Test server',
      description: 'Test description',
      maxPlayers: 50,
      tickRate: 30,
      saveInterval: 300,
      seed: 24680,
      worldSize: 4200,
    }
    api.updateServerConfiguration.mockResolvedValue({
      desired: savedDesiredConfiguration,
      effective: null,
    })
    renderRoute('/dashboard/server')

    fireEvent.change(await screen.findByLabelText(/^Seed/), {
      target: { value: '24680' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Save configuration' }))
    expect(await screen.findByText('Desired configuration saved.')).toBeVisible()

    fireEvent.click(screen.getByRole('link', { name: 'World' }))

    expect(await screen.findByRole('heading', { name: 'World' })).toBeVisible()
    expect(screen.getByLabelText(/^Seed/)).toHaveValue(24680)
  })

  it('renders the Not Found page for an unknown route', () => {
    renderRoute('/dashboard/unknown-route')

    expect(screen.getByRole('heading', { name: 'Page not found' })).toBeVisible()
  })

  it.each([
    '/dashboard/plugins/deploy',
    '/dashboard/plugins/releases',
    '/dashboard/plugins/updates',
    '/dashboard/plugins/updates/package-1',
    '/dashboard/plugins/runtime-changes',
  ])('falls through to Not Found for archived Plugins route %s', (path) => {
    renderRoute(path)

    expect(screen.getByRole('heading', { name: 'Page not found' })).toBeVisible()
  })
})
