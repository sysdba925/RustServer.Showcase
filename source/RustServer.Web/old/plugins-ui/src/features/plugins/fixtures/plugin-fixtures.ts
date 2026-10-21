import type {
  EnvironmentResponse,
  IncomingChangeDiffResponse,
  IncomingChangeResponse,
  ProductionDeploymentResponse,
  ReleaseResponse,
  RuntimeChangeResponse,
  StagingRuntimeChangeDiffResponse,
  StagingRuntimeChangesResponse,
  UpdatePackageResponse,
} from '../../../shared/api'

export const pluginsEnvironment: EnvironmentResponse = {
  environment: 'production',
}

export const releases: ReleaseResponse[] = [
  {
    releaseId: '12',
    pluginsSha: '8bb2a05e9dac7c044ed3',
    oxideConfigsSha: 'c1d0afaa59f3d1b7e045',
    createdAt: '2026-09-29T09:00:00Z',
  },
  {
    releaseId: '11',
    pluginsSha: 'f19bd92e1a4c4b270f01',
    oxideConfigsSha: 'e0d90477ec449f224601',
    createdAt: '2026-09-22T09:00:00Z',
  },
]

export const productionDeployment: ProductionDeploymentResponse = {
  source: 'release',
  releaseId: '11',
  pluginsSha: 'f19bd92e1a4c4b270f01',
  oxideConfigsSha: 'e0d90477ec449f224601',
  deployedFiles: 57,
  deployedAt: '2026-09-24T12:30:00Z',
}

export const inspectedUpdatePackage: UpdatePackageResponse = {
  id: 'update-package-2026-09-29',
  originalFileName: 'RaidableBases-update.zip',
  status: 'inspected',
  createdAt: '2026-09-29T08:30:00Z',
  files: [
    { path: 'oxide/plugins/RaidableBases.cs', classification: 'managed' },
    { path: 'oxide/config/RaidableBases.json', classification: 'managed' },
    { path: 'oxide/data/RaidableBases/runtime.json', classification: 'runtime' },
  ],
  commits: [],
}

export const committedUpdatePackage: UpdatePackageResponse = {
  ...inspectedUpdatePackage,
  status: 'committed',
  commits: [
    {
      repository: 'plugins',
      previousHead: 'f19bd92e1a4c',
      commitSha: '8bb2a05e9dac',
      committedPaths: ['oxide/plugins/RaidableBases.cs'],
    },
    {
      repository: 'oxideConfigs',
      previousHead: 'e0d90477ec44',
      commitSha: 'c1d0afaa59f3',
      committedPaths: ['oxide/config/RaidableBases.json'],
    },
  ],
}

export const failedUpdatePackage: UpdatePackageResponse = {
  ...inspectedUpdatePackage,
  status: 'failed',
  files: [],
}

export const incomingChanges: IncomingChangeResponse[] = [
  {
    path: 'oxide/plugins/RaidableBases.cs',
    repository: 'plugins',
    classification: 'managed',
    changeType: 'modified',
    textDiffAvailable: true,
  },
  {
    path: 'oxide/config/RaidableBases.json',
    repository: 'oxideConfigs',
    classification: 'managed',
    changeType: 'added',
    textDiffAvailable: true,
  },
  {
    path: 'oxide/data/RaidableBases/runtime.json',
    repository: 'oxideConfigs',
    classification: 'runtime',
    changeType: 'modified',
    textDiffAvailable: false,
  },
  {
    path: 'oxide/plugins/OriginalPackage.zip',
    repository: 'plugins',
    classification: 'artifact',
    changeType: 'unchanged',
    textDiffAvailable: false,
  },
]

export const incomingChangeDiff: IncomingChangeDiffResponse = {
  packageId: inspectedUpdatePackage.id,
  path: 'oxide/plugins/RaidableBases.cs',
  repository: 'plugins',
  changeType: 'modified',
  workspaceHead: 'f19bd92e1a4c',
  workspaceText: 'namespace Oxide.Plugins {\n  class RaidableBases {}\n}',
  incomingText: 'namespace Oxide.Plugins {\n  class RaidableBases { /* updated */ }\n}',
}

export const runtimeChanges: RuntimeChangeResponse[] = [
  {
    path: 'oxide/plugins/RaidableBases.cs',
    repository: 'plugins',
    changeType: 'modified',
    textDiffAvailable: true,
    changeToken: 'runtime-token-plugin',
  },
  {
    path: 'oxide/config/RaidableBases.json',
    repository: 'oxideConfigs',
    changeType: 'added',
    textDiffAvailable: true,
    changeToken: 'runtime-token-config',
  },
  {
    path: 'oxide/lang/en/RaidableBases.json',
    repository: 'oxideConfigs',
    changeType: 'deleted',
    textDiffAvailable: false,
    changeToken: 'runtime-token-language',
  },
]

export const runtimeChangeDiff: StagingRuntimeChangeDiffResponse = {
  path: 'oxide/plugins/RaidableBases.cs',
  changeType: 'modified',
  baselineText: 'namespace Oxide.Plugins {\n  class RaidableBases {}\n}',
  runtimeText: 'namespace Oxide.Plugins {\n  class RaidableBases { /* runtime edit */ }\n}',
}

export const stagingRuntimeChanges: StagingRuntimeChangesResponse = {
  source: 'development',
  pluginsSha: '8bb2a05e9dac7c044ed3',
  oxideConfigsSha: 'c1d0afaa59f3d1b7e045',
  changes: runtimeChanges,
}
