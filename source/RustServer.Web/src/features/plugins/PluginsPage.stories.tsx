import type { Meta, StoryObj } from '@storybook/react-vite'

import { HttpError } from '../../shared/api'
import {
  createPluginsApiScenario,
  sourcePreviewWithChanges,
  succeededPluginsOperation,
} from './fixtures/plugins-api-scenarios'
import {
  loadingPluginsDashboard,
  productionPluginsDashboard,
  stagingPluginsDashboard,
  unavailablePluginsDashboard,
} from './fixtures/plugins-dashboard-scenarios'
import { PluginsPage } from './PluginsPage'
import type { GetGitStatusResponse } from '../../shared/api'

type Story = StoryObj<typeof PluginsPage>

const meta = {
  title: 'Plugins/Page',
  component: PluginsPage,
} satisfies Meta<typeof PluginsPage>

export default meta

const environmentRejectedApi = createPluginsApiScenario({
  updateEnvironment: async () => {
    throw new HttpError({
      method: 'PUT',
      url: '/plugins-api/api/environment',
      status: 409,
      statusText: 'Conflict',
      responseBody: {
        code: 'SourceNotAllowed',
        message: 'A Development baseline cannot switch to Production.',
        details: null,
      },
    })
  },
})

function scenarioGitStatus(): GetGitStatusResponse {
  if (stagingPluginsDashboard.gitStatus.status !== 'available') {
    throw new Error('The staging story requires Git status data.')
  }

  return stagingPluginsDashboard.gitStatus.data
}

const stagingGitStatus = scenarioGitStatus()

const gitOperationsDashboard = {
  ...stagingPluginsDashboard,
  gitStatus: {
    status: 'available' as const,
    data: {
      ...stagingGitStatus,
      plugins: {
        ...stagingGitStatus.plugins,
        relation: 'ahead',
      },
      configs: {
        ...stagingGitStatus.configs,
        relation: 'diverged',
      },
    },
  },
}

const gitFastForwardDashboard = {
  ...stagingPluginsDashboard,
  gitStatus: {
    status: 'available' as const,
    data: {
      ...stagingGitStatus,
      plugins: { ...stagingGitStatus.plugins, relation: 'behind' },
    },
  },
}

const failedOperation = {
  ...succeededPluginsOperation,
  status: 'failed',
  completedAt: '2026-10-10T10:15:03Z',
  failure: {
    phase: 'applyRuntime',
    code: 'RuntimeApplyFailed',
    message: 'The managed Runtime write did not complete.',
    repository: 'configs',
    filePath: 'oxide/config/Economics.json',
  },
  rollback: { status: 'succeeded', message: 'Runtime files were restored.' },
}

const interruptedOperation = {
  ...succeededPluginsOperation,
  status: 'interrupted',
  completedAt: '2026-10-10T10:15:03Z',
  failure: {
    phase: 'captureFinalState',
    code: 'OperationInterrupted',
    message: 'The service restarted before the operation completed.',
    repository: null,
    filePath: null,
  },
  rollback: { status: 'notAttempted', message: 'Recovery requires operator review.' },
}

export const Staging: Story = { args: { dashboard: stagingPluginsDashboard } }
export const Production: Story = { args: { dashboard: productionPluginsDashboard } }
export const GitStatuses: Story = {
  args: {
    dashboard: {
      ...stagingPluginsDashboard,
      gitStatus: {
        status: 'available',
        data: {
          gitStatus: 'ready',
          lastFetchAt: '2026-10-08T10:00:00Z',
          plugins: { configured: true, localBranch: 'develop', localHead: 'abc123456789', remoteBranch: 'origin/develop', remoteHead: 'abc123456789', workspace: 'clean', relation: 'behind', diagnostic: null },
          configs: { configured: true, localBranch: 'develop', localHead: 'def987654321', remoteBranch: 'origin/develop', remoteHead: 'def987654321', workspace: 'clean', relation: 'diverged', diagnostic: null },
        },
      },
    },
  },
}
export const RuntimeChanges: Story = {
  args: {
    dashboard: {
      ...stagingPluginsDashboard,
      runtimeDiff: {
        status: 'available',
        data: {
          status: 'ready',
          changes: [
            { repository: 'plugins', path: 'oxide/plugins/GatherManager.cs', change: 'modified' },
            { repository: 'configs', path: 'oxide/config/Economics.json', change: 'added' },
            { repository: 'configs', path: 'oxide/config/Old.json', change: 'deleted' },
          ],
        },
      },
    },
    api: createPluginsApiScenario(),
  },
}
/** Open Runtime Diff to inspect content, select files, and review Commit or Revert confirmations. */
export const ProductionRuntimeChanges: Story = {
  args: {
    dashboard: {
      ...productionPluginsDashboard,
      runtimeDiff: {
        status: 'available',
        data: {
          status: 'ready',
          changes: [{ repository: 'configs', path: 'oxide/config/Economics.json', change: 'modified' }],
        },
      },
    },
    api: createPluginsApiScenario(),
  },
}
export const Loading: Story = { args: { dashboard: loadingPluginsDashboard } }
export const Unavailable: Story = { args: { dashboard: unavailablePluginsDashboard } }

/** Open Environment and choose Production to review the explicit confirmation state. */
export const EnvironmentSwitchConfirmation: Story = {
  args: { dashboard: stagingPluginsDashboard, api: createPluginsApiScenario() },
}

/** Uses the actual Plugins error envelope for the 409 Environment rejection. */
export const EnvironmentSwitchRejectedByBackend: Story = {
  args: { dashboard: stagingPluginsDashboard, api: environmentRejectedApi },
}

/** Open Fetch Git and confirm to observe the returned persistent operation report. */
export const GitFetch: Story = {
  args: { dashboard: stagingPluginsDashboard, api: createPluginsApiScenario() },
}

/** Rows expose the backend-selected Push and Merge + Push commands for separate repositories. */
export const GitPushAndMergePush: Story = {
  args: { dashboard: gitOperationsDashboard, api: createPluginsApiScenario() },
}

/** The reported behind relation exposes Fast-forward through the same Git Sync endpoint. */
export const GitFastForward: Story = {
  args: { dashboard: gitFastForwardDashboard, api: createPluginsApiScenario() },
}

/** Open Switch Source; the Production snapshot limits the draft to a complete Release. */
export const SourceSelection: Story = {
  args: { dashboard: productionPluginsDashboard, api: createPluginsApiScenario() },
}

/** Mocked Preview response shows the exact changed managed files and target Git SHAs. */
export const SourcePreviewWithChanges: Story = {
  args: {
    dashboard: productionPluginsDashboard,
    api: createPluginsApiScenario({ previewSource: async () => sourcePreviewWithChanges }),
  },
}

/** Use Apply Source to request Preview first, then explicitly confirm the mock Apply. */
export const ApplySourceConfirmation: Story = {
  args: { dashboard: stagingPluginsDashboard, api: createPluginsApiScenario() },
}

/** The operation endpoint remains inProgress, keeping duplicate mutation controls locked. */
export const OperationRunning: Story = {
  args: {
    dashboard: stagingPluginsDashboard,
    api: createPluginsApiScenario({
      getOperation: async () => ({ ...succeededPluginsOperation, status: 'inProgress', completedAt: null, currentPhase: 'applyRuntime' }),
    }),
  },
}

export const OperationSucceeded: Story = {
  args: { dashboard: stagingPluginsDashboard, api: createPluginsApiScenario() },
}

export const OperationFailedWithRollback: Story = {
  args: {
    dashboard: stagingPluginsDashboard,
    api: createPluginsApiScenario({ getOperation: async () => failedOperation }),
  },
}

export const OperationInterrupted: Story = {
  args: {
    dashboard: stagingPluginsDashboard,
    api: createPluginsApiScenario({ getOperation: async () => interruptedOperation }),
  },
}
