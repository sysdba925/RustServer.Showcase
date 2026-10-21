import type { PluginsDashboardState } from '../model/plugins-dashboard'

export const stagingPluginsDashboard: PluginsDashboardState = {
  environment: { status: 'available', data: { environment: 'staging', systemState: 'ready' } },
  baseline: { status: 'available', data: { status: 'ready', baseline: { environment: 'staging', source: 'development', release: null, pluginsCommit: 'abc123456789', configsCommit: 'def987654321', appliedAt: '2026-10-08T10:00:00Z' } } },
  gitStatus: { status: 'available', data: { gitStatus: 'ready', lastFetchAt: '2026-10-08T10:00:00Z', plugins: { configured: true, localBranch: 'develop', localHead: 'abc123456789', remoteBranch: 'origin/develop', remoteHead: 'abc123456789', workspace: 'clean', relation: 'equal', diagnostic: null }, configs: { configured: true, localBranch: 'develop', localHead: 'def987654321', remoteBranch: 'origin/develop', remoteHead: 'def987654321', workspace: 'clean', relation: 'equal', diagnostic: null } } },
  runtimeDiff: { status: 'available', data: { status: 'ready', changes: [] } },
  releases: { status: 'available', data: { releases: [] } },
}

export const productionPluginsDashboard: PluginsDashboardState = {
  ...stagingPluginsDashboard,
  environment: { status: 'available', data: { environment: 'production', systemState: 'ready' } },
  baseline: { status: 'available', data: { status: 'ready', baseline: { environment: 'production', source: 'release', release: '3.2.0', pluginsCommit: 'e1f2a3b4c5d6', configsCommit: 'f6e5d4c3b2a1', appliedAt: '2026-10-09T13:30:00Z' } } },
  releases: { status: 'available', data: { releases: [{ version: '3.2.0', branch: 'release/3.2.0', status: 'complete', current: true, matchesBaseline: true, pluginsHead: 'e1f2a3b4c5d6', configsHead: 'f6e5d4c3b2a1' }] } },
}

export const loadingPluginsDashboard: PluginsDashboardState = {
  environment: { status: 'loading' }, baseline: { status: 'loading' }, gitStatus: { status: 'loading' }, runtimeDiff: { status: 'loading' }, releases: { status: 'loading' },
}

export const unavailablePluginsDashboard: PluginsDashboardState = {
  environment: { status: 'unavailable', message: 'Plugins API is unavailable.' }, baseline: { status: 'unavailable', message: 'Plugins API is unavailable.' }, gitStatus: { status: 'unavailable', message: 'Plugins API is unavailable.' }, runtimeDiff: { status: 'unavailable', message: 'Plugins API is unavailable.' }, releases: { status: 'unavailable', message: 'Plugins API is unavailable.' },
}
