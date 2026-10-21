import type {
  ControlPlaneInput,
  Operation,
  OxideVersionInfo,
  ResourceState,
  RustVersionInfo,
  ServerConfigurationResponse,
  ServerStatus,
  StartupSchemaResponse,
  StartupSnapshotResponse,
} from '../model'

export type ControlPlaneScenarioName =
  | 'ready'
  | 'stopped'
  | 'starting'
  | 'rust-update-available'
  | 'oxide-update-available'
  | 'maintenance'
  | 'update-failed'
  | 'partial-unavailable'
  | 'inconsistent-status'

export const serverReady: ServerStatus = {
  isRunning: true,
  isReady: true,
}

export const serverStopped: ServerStatus = {
  isRunning: false,
  isReady: false,
}

export const serverStarting: ServerStatus = {
  isRunning: true,
  isReady: false,
}

export const serverInconsistent: ServerStatus = {
  isRunning: false,
  isReady: true,
}

export const serverStatusUnavailable: ResourceState<ServerStatus> = {
  status: 'unavailable',
  message: 'Control service did not respond.',
}

export const rustVersionRunning: RustVersionInfo = {
  branch: 'public',
  installedBuildId: 12345678,
  running: {
    protocol: '2633.288.1',
    buildDate: '2026-09-22T07:48:10',
    unityVersion: '6000.3.15x1-11',
    changeset: 165217,
    branch: 'release',
  },
  availableBuildId: 12345678,
  updateAvailable: false,
}

export const rustVersionStopped: RustVersionInfo = {
  ...rustVersionRunning,
  running: null,
}

export const oxideVersionCurrent: OxideVersionInfo = {
  installedVersion: '2.0.7726',
  runningVersion: '2.0.7726',
  availableVersion: '2.0.7726',
  updateAvailable: false,
}

export const oxideVersionStopped: OxideVersionInfo = {
  ...oxideVersionCurrent,
  runningVersion: null,
}

export const noKnownOperation: ResourceState<Operation | null> = {
  status: 'available',
  data: null,
}

export const activeRustUpdateOperation: Operation = {
  id: 'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
  type: 'RustUpdate',
  status: 'Running',
  createdAt: '2026-09-29T08:30:00Z',
  startedAt: '2026-09-29T08:30:02Z',
  finishedAt: null,
  exitCode: null,
  error: null,
}

export const activeOxideUpdateOperation: Operation = {
  id: 'd39c474c-5d54-4b7e-b18a-6bf3a1a2c33d',
  type: 'OxideUpdate',
  status: 'Pending',
  createdAt: '2026-09-29T08:45:00Z',
  startedAt: null,
  finishedAt: null,
  exitCode: null,
  error: null,
}

export const failedRustUpdateOperation: Operation = {
  id: '5c051a2f-4a71-458f-a00e-4fcd6f0d43d4',
  type: 'RustUpdate',
  status: 'Failed',
  createdAt: '2026-09-29T07:10:00Z',
  startedAt: '2026-09-29T07:10:03Z',
  finishedAt: '2026-09-29T07:11:07Z',
  exitCode: 1,
  error: 'SteamCMD exited with code 1.',
}

export const activeWipeOperation: Operation = {
  id: '9c9b5fc7-f7d0-44f8-af9f-4f13e7a29f4d',
  type: 'Wipe',
  status: 'Running',
  createdAt: '2026-09-30T08:30:00Z',
  startedAt: '2026-09-30T08:30:02Z',
  finishedAt: null,
  exitCode: null,
  error: null,
}

export const completedWipeOperation: Operation = {
  id: '477948cb-c48c-4d81-b1ad-f08ad42c7d4e',
  type: 'Wipe',
  status: 'Succeeded',
  createdAt: '2026-09-30T08:30:00Z',
  startedAt: '2026-09-30T08:30:02Z',
  finishedAt: '2026-09-30T08:31:09Z',
  exitCode: 0,
  error: null,
  wipeResult: {
    succeeded: true,
    targets: [
      {
        target: 'world',
        action: 'delete',
        outcome: 'deleted',
        affectedItems: 1,
      },
      {
        target: 'blueprints',
        action: 'keep',
        outcome: 'preserved',
        affectedItems: 0,
      },
    ],
  },
}

export const serverConfiguration: ServerConfigurationResponse = {
  desired: {
    hostname: 'RustServer Staging',
    description: 'Managed Rust server',
    maxPlayers: 50,
    tickRate: 30,
    saveInterval: 300,
    seed: 12345,
    worldSize: 4200,
  },
  effective: {
    hostname: 'RustServer Current',
    description: 'Previous server configuration',
    maxPlayers: 25,
    tickRate: 30,
    saveInterval: 600,
    seed: 67890,
    worldSize: 3500,
  },
}

export const serverConfigurationUnconfirmed: ServerConfigurationResponse = {
  ...serverConfiguration,
  effective: null,
}

export const startupSchema: StartupSchemaResponse = {
  version: 1,
  milestones: [
    { id: 'engine', label: 'Unity Engine' },
    { id: 'mono', label: 'Mono Runtime' },
    { id: 'oxide', label: 'Oxide Core' },
    { id: 'bootstrap', label: 'Server bootstrap' },
    { id: 'network', label: 'Network initialization' },
    { id: 'world', label: 'World initialization' },
    { id: 'plugins', label: 'Plugin initialization' },
    { id: 'listening', label: 'Server listening' },
    { id: 'steam', label: 'Steam server connection' },
  ],
}

export const noActiveStartup: StartupSnapshotResponse = {
  sessionId: null,
  started: false,
  milestones: [],
}

export const startupInProgress: StartupSnapshotResponse = {
  sessionId: 'startup-2026-09-29',
  started: false,
  milestones: [
    'reached',
    'skipped',
    'reached',
    'reached',
    'reached',
    'reached',
    'reached',
    'reached',
    'pending',
  ],
}

export const startupCompleted: StartupSnapshotResponse = {
  sessionId: 'startup-2026-09-29',
  started: true,
  milestones: [
    'reached',
    'reached',
    'reached',
    'reached',
    'reached',
    'reached',
    'reached',
    'reached',
    'reached',
  ],
}

export const controlPlaneScenarios = {
  ready: {
    serverStatus: { status: 'available', data: serverReady },
    rustVersions: { status: 'available', data: rustVersionRunning },
    oxideVersions: { status: 'available', data: oxideVersionCurrent },
    operationDetail: noKnownOperation,
  },
  stopped: {
    serverStatus: { status: 'available', data: serverStopped },
    rustVersions: { status: 'available', data: rustVersionStopped },
    oxideVersions: { status: 'available', data: oxideVersionStopped },
    operationDetail: noKnownOperation,
  },
  starting: {
    serverStatus: { status: 'available', data: serverStarting },
    rustVersions: {
      status: 'unavailable',
      message: 'Rust runtime metadata is not available while the server starts.',
    },
    oxideVersions: {
      status: 'unavailable',
      message: 'Oxide runtime metadata is not available while the server starts.',
    },
    operationDetail: noKnownOperation,
  },
  'rust-update-available': {
    serverStatus: { status: 'available', data: serverStopped },
    rustVersions: {
      status: 'available',
      data: {
        ...rustVersionStopped,
        installedBuildId: 12340000,
        availableBuildId: 12345678,
        updateAvailable: true,
      },
    },
    oxideVersions: { status: 'available', data: oxideVersionStopped },
    operationDetail: noKnownOperation,
  },
  'oxide-update-available': {
    serverStatus: { status: 'available', data: serverStopped },
    rustVersions: { status: 'available', data: rustVersionStopped },
    oxideVersions: {
      status: 'available',
      data: {
        ...oxideVersionStopped,
        installedVersion: '2.0.7725',
        availableVersion: '2.0.7726',
        updateAvailable: true,
      },
    },
    operationDetail: noKnownOperation,
  },
  maintenance: {
    serverStatus: { status: 'available', data: serverStopped },
    rustVersions: { status: 'available', data: rustVersionStopped },
    oxideVersions: { status: 'available', data: oxideVersionStopped },
    operationDetail: { status: 'available', data: activeRustUpdateOperation },
  },
  'update-failed': {
    serverStatus: { status: 'available', data: serverStopped },
    rustVersions: {
      status: 'available',
      data: {
        ...rustVersionStopped,
        installedBuildId: 12340000,
        availableBuildId: 12345678,
        updateAvailable: true,
      },
    },
    oxideVersions: { status: 'available', data: oxideVersionStopped },
    operationDetail: { status: 'available', data: failedRustUpdateOperation },
  },
  'partial-unavailable': {
    serverStatus: { status: 'available', data: serverReady },
    rustVersions: {
      status: 'unavailable',
      message: 'Rust version service did not respond.',
    },
    oxideVersions: { status: 'available', data: oxideVersionCurrent },
    operationDetail: noKnownOperation,
  },
  'inconsistent-status': {
    serverStatus: { status: 'available', data: serverInconsistent },
    rustVersions: { status: 'available', data: rustVersionStopped },
    oxideVersions: { status: 'available', data: oxideVersionStopped },
    operationDetail: noKnownOperation,
  },
} satisfies Record<ControlPlaneScenarioName, ControlPlaneInput>
