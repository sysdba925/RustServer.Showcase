import type {
  GetPluginsOperationResponse,
  SourcePreviewResponse,
} from '../../../shared/api'
import type { PluginsApi } from '../api'
import { stagingPluginsDashboard } from './plugins-dashboard-scenarios'

const stagingBaseline = stagingPluginsDashboard.baseline.status === 'available'
  ? stagingPluginsDashboard.baseline.data.baseline
  : null

export const sourcePreviewWithChanges: SourcePreviewResponse = {
  environment: 'staging',
  currentSource: { type: 'development', release: null },
  currentSourceDiagnostic: null,
  baseline: stagingBaseline,
  targetSource: { type: 'release', release: '3.2.0' },
  targetGit: { pluginsHead: 'f2a3b4c5d6e7', configsHead: 'a7b6c5d4e3f2' },
  counts: { added: 1, modified: 2, deleted: 1 },
  changes: [
    { repository: 'plugins', path: 'oxide/plugins/GatherManager.cs', change: 'modified' },
    { repository: 'configs', path: 'oxide/config/Economics.json', change: 'added' },
    { repository: 'plugins', path: 'oxide/plugins/OldPlugin.cs', change: 'deleted' },
  ],
}

export const succeededPluginsOperation: GetPluginsOperationResponse = {
  operationId: 'op-plugins-001',
  type: 'applySource',
  status: 'succeeded',
  currentPhase: 'completed',
  source: { type: 'release', release: '3.2.0' },
  steps: [
    { name: 'fetchPlugins', status: 'completed' },
    { name: 'fetchConfigs', status: 'completed' },
    { name: 'applyRuntime', status: 'completed' },
  ],
  failure: null,
  rollback: { status: 'notAttempted', message: null },
  counts: { added: 1, modified: 2, deleted: 1 },
  changes: sourcePreviewWithChanges.changes,
  sync: null,
  finalGitState: [
    { repository: 'plugins', branch: 'release/3.2.0', head: 'f2a3b4c5d6e7', relation: 'equal', diagnostic: null },
    { repository: 'configs', branch: 'release/3.2.0', head: 'a7b6c5d4e3f2', relation: 'equal', diagnostic: null },
  ],
  previousBaseline: stagingBaseline,
  finalBaseline: stagingBaseline === null
    ? null
    : { ...stagingBaseline, source: 'release', release: '3.2.0' },
  startedAt: '2026-10-10T10:15:00Z',
  completedAt: '2026-10-10T10:15:03Z',
}

/** A contract-shaped mock API for interaction stories and focused component tests. */
export function createPluginsApiScenario(
  overrides: Partial<PluginsApi> = {},
): PluginsApi {
  const dashboard = stagingPluginsDashboard

  return {
    getEnvironment: async () => dashboard.environment.status === 'available'
      ? dashboard.environment.data
      : Promise.reject(new Error('Environment unavailable.')),
    getBaseline: async () => dashboard.baseline.status === 'available'
      ? dashboard.baseline.data
      : Promise.reject(new Error('Baseline unavailable.')),
    getGitStatus: async () => dashboard.gitStatus.status === 'available'
      ? dashboard.gitStatus.data
      : Promise.reject(new Error('Git status unavailable.')),
    getRuntimeDiff: async () => dashboard.runtimeDiff.status === 'available'
      ? dashboard.runtimeDiff.data
      : Promise.reject(new Error('Runtime Diff unavailable.')),
    getRuntimeDiffContent: async (repository, path) => ({
      status: 'ready',
      content: {
        repository,
        path,
        change: 'modified',
        baseline: { kind: 'text', text: '{"enabled": false}' },
        runtime: { kind: 'text', text: '{"enabled": true}' },
      },
    }),
    getReleases: async () => dashboard.releases.status === 'available'
      ? dashboard.releases.data
      : Promise.reject(new Error('Releases unavailable.')),
    updateEnvironment: async (request) => ({ environment: request.environment ?? 'staging' }),
    fetchGit: async () => ({ operationId: succeededPluginsOperation.operationId }),
    syncGit: async () => ({ operationId: succeededPluginsOperation.operationId }),
    previewSource: async () => sourcePreviewWithChanges,
    applySource: async () => ({ operationId: succeededPluginsOperation.operationId }),
    commitRuntime: async () => ({ operationId: succeededPluginsOperation.operationId }),
    revertRuntime: async () => ({ operationId: succeededPluginsOperation.operationId }),
    revertAllRuntime: async () => ({ operationId: succeededPluginsOperation.operationId }),
    getOperations: async () => ({ items: [], nextCursor: null }),
    getOperation: async () => succeededPluginsOperation,
    ...overrides,
  }
}
