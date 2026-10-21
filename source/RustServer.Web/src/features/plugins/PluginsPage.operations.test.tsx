import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  HttpError,
  type GetGitStatusResponse,
  type GetPluginsOperationResponse,
} from '../../shared/api'
import {
  createPluginsApiScenario,
  sourcePreviewWithChanges,
  succeededPluginsOperation,
} from './fixtures/plugins-api-scenarios'
import {
  productionPluginsDashboard,
  stagingPluginsDashboard,
} from './fixtures/plugins-dashboard-scenarios'
import { PluginsPage } from './PluginsPage'

function gitStatusWithRelation(relation: string): GetGitStatusResponse {
  if (stagingPluginsDashboard.gitStatus.status !== 'available') {
    throw new Error('The staging fixture requires Git status data.')
  }

  return {
    ...stagingPluginsDashboard.gitStatus.data,
    plugins: { ...stagingPluginsDashboard.gitStatus.data.plugins, relation },
  }
}

function dashboardWithGitRelation(relation: string) {
  return {
    ...stagingPluginsDashboard,
    gitStatus: { status: 'available' as const, data: gitStatusWithRelation(relation) },
  }
}

const runtimeChangesDashboard = {
  ...stagingPluginsDashboard,
  runtimeDiff: {
    status: 'available' as const,
    data: {
      status: 'ready',
      changes: [
        { repository: 'plugins', path: 'oxide/plugins/GatherManager.cs', change: 'modified' },
        { repository: 'configs', path: 'oxide/config/Economics.json', change: 'added' },
        { repository: 'configs', path: 'oxide/config/Old.json', change: 'deleted' },
      ],
    },
  },
}

function openFetchConfirmation(): void {
  fireEvent.click(screen.getByRole('button', { name: '↻ Fetch Git' }))
  expect(screen.getByRole('dialog', { name: 'Fetch Git' })).toBeVisible()
}

afterEach(() => vi.useRealTimers())

describe('Plugins management workflows', () => {
  it('confirms the target Environment, sends the published request, and refreshes it', async () => {
    const updateEnvironment = vi.fn().mockResolvedValue({ environment: 'production' })
    const getEnvironment = vi.fn().mockResolvedValue({ environment: 'production', systemState: 'ready' })
    const getBaseline = vi.fn().mockResolvedValue(
      stagingPluginsDashboard.baseline.status === 'available'
        ? stagingPluginsDashboard.baseline.data
        : null,
    )
    const api = createPluginsApiScenario({ updateEnvironment, getEnvironment, getBaseline })

    render(<PluginsPage api={api} dashboard={stagingPluginsDashboard} />)

    fireEvent.click(screen.getByRole('button', { name: /Environment/ }))
    fireEvent.click(screen.getByRole('button', { name: /Production/ }))
    expect(screen.getByText(/Target environment:/)).toHaveTextContent('Production')
    fireEvent.click(screen.getByRole('button', { name: 'Confirm environment' }))

    await waitFor(() => {
      expect(updateEnvironment).toHaveBeenCalledWith(
        { environment: 'production' },
        expect.any(AbortSignal),
      )
    })
    expect(getEnvironment).toHaveBeenCalledTimes(1)
    expect(getBaseline).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('keeps the Environment dialog open and exposes an API conflict', async () => {
    const api = createPluginsApiScenario({
      updateEnvironment: async () => {
        throw new HttpError({
          method: 'PUT',
          url: '/plugins-api/api/environment',
          status: 409,
          statusText: 'Conflict',
          responseBody: {
            code: 'SourceNotAllowed',
            message: 'Apply a Release before selecting Production.',
            details: null,
          },
        })
      },
    })

    render(<PluginsPage api={api} dashboard={stagingPluginsDashboard} />)

    fireEvent.click(screen.getByRole('button', { name: /Environment/ }))
    fireEvent.click(screen.getByRole('button', { name: /Production/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Confirm environment' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'SourceNotAllowed: Apply a Release before selecting Production.',
    )
    expect(screen.getByRole('dialog', { name: 'Change Environment' })).toBeVisible()
  })

  it('uses Git Sync for a reported fast-forward instead of inventing an action endpoint', async () => {
    const syncGit = vi.fn().mockResolvedValue({ operationId: 'op-plugins-001' })
    const getGitStatus = vi.fn().mockResolvedValue(gitStatusWithRelation('behind'))
    const api = createPluginsApiScenario({ syncGit, getGitStatus })

    render(<PluginsPage api={api} dashboard={dashboardWithGitRelation('behind')} />)

    fireEvent.click(screen.getByRole('button', { name: 'Plugins ⇣ Fast-forward' }))
    expect(screen.getByText('Reported relation')).toBeVisible()
    fireEvent.click(screen.getByRole('button', { name: 'Synchronize repository' }))

    await waitFor(() => {
      expect(syncGit).toHaveBeenCalledWith(
        { repository: 'plugins' },
        expect.any(AbortSignal),
      )
    })
    expect(await screen.findByText('Succeeded')).toBeVisible()
    expect(getGitStatus).toHaveBeenCalledTimes(1)
  })

  it('previews a selected Release, then applies the same contract target after confirmation', async () => {
    const previewSource = vi.fn().mockResolvedValue(sourcePreviewWithChanges)
    const applySource = vi.fn().mockResolvedValue({ operationId: 'op-plugins-001' })
    const getRuntimeDiff = vi.fn().mockResolvedValue(
      stagingPluginsDashboard.runtimeDiff.status === 'available'
        ? stagingPluginsDashboard.runtimeDiff.data
        : null,
    )
    const getReleases = vi.fn().mockResolvedValue(
      productionPluginsDashboard.releases.status === 'available'
        ? productionPluginsDashboard.releases.data
        : null,
    )
    const api = createPluginsApiScenario({
      previewSource,
      applySource,
      getRuntimeDiff,
      getReleases,
    })

    render(<PluginsPage api={api} dashboard={productionPluginsDashboard} />)

    fireEvent.click(screen.getByRole('button', { name: /Switch Source/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Preview Source' }))

    await waitFor(() => {
      expect(previewSource).toHaveBeenCalledWith(
        { source: 'release', release: '3.2.0' },
        expect.any(AbortSignal),
      )
    })
    expect(await screen.findByText(/oxide\/config\/Economics\.json/)).toBeVisible()
    fireEvent.click(screen.getByRole('button', { name: 'Confirm Apply' }))

    await waitFor(() => {
      expect(applySource).toHaveBeenCalledWith(
        { source: 'release', release: '3.2.0' },
        expect.any(AbortSignal),
      )
    })
    expect(await screen.findByText('Succeeded')).toBeVisible()
    expect(getRuntimeDiff).toHaveBeenCalledTimes(1)
    expect(getReleases).toHaveBeenCalledTimes(1)
  })

  it('shows complete and incomplete releases, including the missing repository, without making incomplete releases selectable', () => {
    const dashboard = {
      ...productionPluginsDashboard,
      releases: {
        status: 'available' as const,
        data: {
          releases: [
            { version: '3.2.0', branch: 'release/3.2.0', status: 'complete', current: true, matchesBaseline: true, pluginsHead: 'e1f2a3b4c5d6', configsHead: 'f6e5d4c3b2a1' },
            { version: '3.3.0', branch: 'release/3.3.0', status: 'incomplete', current: false, matchesBaseline: null, pluginsHead: '123456789abc', configsHead: null },
          ],
        },
      },
    }

    render(<PluginsPage api={createPluginsApiScenario()} dashboard={dashboard} />)

    fireEvent.click(screen.getByRole('button', { name: /Switch Source/ }))

    expect(screen.getByText('Complete')).toBeVisible()
    expect(screen.getByText('Incomplete')).toBeVisible()
    expect(screen.getAllByText('✓ Plugins — branch found')).toHaveLength(2)
    expect(screen.getByText('✕ Configs — branch missing')).toBeVisible()
    expect(screen.getByText('Unavailable: missing Configs release branch.')).toBeVisible()
    expect(screen.getByRole('button', { name: /release\/3\.3\.0/i })).toBeDisabled()
  })

  it('opens the Release tab for incomplete diagnostics without enabling Preview', () => {
    const dashboard = {
      ...stagingPluginsDashboard,
      releases: {
        status: 'available' as const,
        data: {
          releases: [
            { version: '3.2.0', branch: 'release/3.2.0', status: 'incomplete', current: false, matchesBaseline: null, pluginsHead: null, configsHead: 'f6e5d4c3b2a1' },
          ],
        },
      },
    }

    render(<PluginsPage api={createPluginsApiScenario()} dashboard={dashboard} />)

    fireEvent.click(screen.getByRole('button', { name: /Switch Source/ }))

    const releaseTab = screen.getByRole('button', { name: /Release/ })
    expect(releaseTab).toBeEnabled()
    fireEvent.click(releaseTab)

    expect(screen.getByRole('button', { name: /release\/3\.2\.0/i })).toBeDisabled()
    expect(screen.getByText('✕ Plugins — branch missing')).toBeVisible()
    expect(screen.getByRole('button', { name: 'Preview Source' })).toBeDisabled()
  })

  it('refreshes only Runtime Diff data and exposes loading and error states', async () => {
    let resolveRuntimeDiff: ((value: typeof runtimeChangesDashboard.runtimeDiff.data) => void) | undefined
    const getRuntimeDiff = vi.fn().mockImplementation(() => new Promise<typeof runtimeChangesDashboard.runtimeDiff.data>((resolve) => {
      resolveRuntimeDiff = resolve
    }))
    const api = createPluginsApiScenario({ getRuntimeDiff })

    render(<PluginsPage api={api} dashboard={runtimeChangesDashboard} />)

    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))

    expect(getRuntimeDiff).toHaveBeenCalledWith(expect.any(AbortSignal))
    expect(screen.getByRole('button', { name: 'Refreshing…' })).toBeDisabled()
    expect(screen.getByText('Loading…')).toBeVisible()

    resolveRuntimeDiff?.({ status: 'ready', changes: [] })

    await waitFor(() => {
      expect(screen.getByText('Up to date')).toBeVisible()
    })

    const failedRefresh = vi.fn().mockRejectedValue(new Error('Runtime storage is unavailable.'))
    const errorApi = createPluginsApiScenario({ getRuntimeDiff: failedRefresh })
    render(<PluginsPage api={errorApi} dashboard={runtimeChangesDashboard} />)

    fireEvent.click(screen.getAllByRole('button', { name: 'Refresh' })[1])

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Runtime Diff refresh failed: Runtime storage is unavailable.',
    )
  })

  it('opens V15 Runtime Diff, defaults to selecting changes, and fetches a file content comparison', async () => {
    const getRuntimeDiffContent = vi.fn().mockResolvedValue({
      status: 'ready',
      content: {
        repository: 'plugins',
        path: 'oxide/plugins/GatherManager.cs',
        change: 'modified',
        baseline: { kind: 'text', text: 'class GatherManager { }\nbool enabled = false;\n}' },
        runtime: { kind: 'text', text: 'class GatherManager { }\nbool enabled = true;\n}' },
      },
    })
    const api = createPluginsApiScenario({ getRuntimeDiffContent })

    render(<PluginsPage api={api} dashboard={runtimeChangesDashboard} />)

    fireEvent.click(screen.getByRole('button', { name: /Runtime Diff/ }))

    expect(screen.getByRole('dialog', { name: 'Runtime Diff' })).toBeVisible()
    expect(screen.getByLabelText('Select oxide/plugins/GatherManager.cs')).toBeChecked()
    expect(screen.getByText('Added')).toBeVisible()
    expect(screen.getByText('Deleted')).toBeVisible()

    fireEvent.click(screen.getByRole('button', { name: 'oxide/plugins/GatherManager.cs' }))

    await waitFor(() => {
      expect(getRuntimeDiffContent).toHaveBeenCalledWith(
        'plugins',
        'oxide/plugins/GatherManager.cs',
        expect.any(AbortSignal),
      )
    })
    const lineDiff = await screen.findByLabelText('Line-by-line comparison')
    expect(lineDiff).toHaveTextContent('Baseline')
    expect(lineDiff).toHaveTextContent('Runtime')
    expect(lineDiff).toHaveTextContent('class GatherManager { }')
    expect(lineDiff).toHaveTextContent('−bool enabled = false;')
    expect(lineDiff).toHaveTextContent('+bool enabled = true;')
  })

  it('renders Added and Deleted files as complete green or red line diffs', async () => {
    const getRuntimeDiffContent = vi.fn().mockImplementation(async (_repository: string, path: string) => ({
      status: 'ready',
      content: path.endsWith('Economics.json')
        ? {
            repository: 'configs',
            path,
            change: 'added',
            baseline: { kind: 'missing', text: null },
            runtime: { kind: 'text', text: '{\n  "enabled": true\n}' },
          }
        : {
            repository: 'configs',
            path,
            change: 'deleted',
            baseline: { kind: 'text', text: '{\n  "legacy": true\n}' },
            runtime: { kind: 'missing', text: null },
          },
    }))
    const api = createPluginsApiScenario({ getRuntimeDiffContent })

    render(<PluginsPage api={api} dashboard={runtimeChangesDashboard} />)

    fireEvent.click(screen.getByRole('button', { name: /Runtime Diff/ }))
    fireEvent.click(screen.getByRole('button', { name: 'oxide/config/Economics.json' }))

    const addedDiff = await screen.findByLabelText('Line-by-line comparison')
    expect(addedDiff).toHaveTextContent('+{')
    expect(addedDiff).toHaveTextContent('+ "enabled": true')

    fireEvent.click(screen.getByRole('button', { name: 'Back to Runtime Diff' }))
    fireEvent.click(screen.getByRole('button', { name: 'oxide/config/Old.json' }))

    const deletedDiff = await screen.findByLabelText('Line-by-line comparison')
    expect(deletedDiff).toHaveTextContent('−{')
    expect(deletedDiff).toHaveTextContent('− "legacy": true')
  })

  it('retains the explicit missing and binary states in a two-column content comparison', async () => {
    const getRuntimeDiffContent = vi.fn().mockResolvedValue({
      status: 'ready',
      content: {
        repository: 'configs',
        path: 'oxide/config/Economics.json',
        change: 'added',
        baseline: { kind: 'missing', text: null },
        runtime: { kind: 'binary', text: null },
      },
    })
    const api = createPluginsApiScenario({ getRuntimeDiffContent })

    render(<PluginsPage api={api} dashboard={runtimeChangesDashboard} />)

    fireEvent.click(screen.getByRole('button', { name: /Runtime Diff/ }))
    fireEvent.click(screen.getByRole('button', { name: 'oxide/config/Economics.json' }))

    expect(await screen.findByText('No file')).toBeVisible()
    expect(screen.getByText('Binary content cannot be shown.')).toBeVisible()
  })

  it('commits only selected Runtime Diff entries, then refreshes Git and Runtime observations', async () => {
    const commitRuntime = vi.fn().mockResolvedValue({ operationId: 'op-runtime-commit' })
    const getGitStatus = vi.fn().mockResolvedValue(
      stagingPluginsDashboard.gitStatus.status === 'available'
        ? stagingPluginsDashboard.gitStatus.data
        : null,
    )
    const getRuntimeDiff = vi.fn().mockResolvedValue(
      stagingPluginsDashboard.runtimeDiff.status === 'available'
        ? stagingPluginsDashboard.runtimeDiff.data
        : null,
    )
    const getBaseline = vi.fn().mockResolvedValue(
      stagingPluginsDashboard.baseline.status === 'available'
        ? stagingPluginsDashboard.baseline.data
        : null,
    )
    const api = createPluginsApiScenario({ commitRuntime, getBaseline, getGitStatus, getRuntimeDiff })

    render(<PluginsPage api={api} dashboard={runtimeChangesDashboard} />)

    fireEvent.click(screen.getByRole('button', { name: /Runtime Diff/ }))
    fireEvent.click(screen.getByLabelText('Select oxide/config/Economics.json'))
    fireEvent.click(screen.getByRole('button', { name: 'Commit selected + Push' }))
    expect(screen.queryByText(/Rust must be stopped/i)).not.toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Commit message'), { target: { value: 'Keep the runtime hotfix' } })
    fireEvent.click(screen.getByRole('button', { name: 'Commit + Push' }))

    await waitFor(() => {
      expect(commitRuntime).toHaveBeenCalledWith({
        message: 'Keep the runtime hotfix',
        changes: [
          { repository: 'plugins', path: 'oxide/plugins/GatherManager.cs' },
          { repository: 'configs', path: 'oxide/config/Old.json' },
        ],
      }, expect.any(AbortSignal))
    })
    expect(await screen.findByText('Succeeded')).toBeVisible()
    expect(getBaseline).toHaveBeenCalledTimes(1)
    expect(getGitStatus).toHaveBeenCalledTimes(1)
    expect(getRuntimeDiff).toHaveBeenCalledTimes(1)
  })

  it('reverts only selected Runtime Diff entries after explicit confirmation', async () => {
    const revertRuntime = vi.fn().mockResolvedValue({ operationId: 'op-runtime-revert-selected' })
    const getRuntimeDiff = vi.fn().mockResolvedValue(
      stagingPluginsDashboard.runtimeDiff.status === 'available'
        ? stagingPluginsDashboard.runtimeDiff.data
        : null,
    )
    const api = createPluginsApiScenario({ revertRuntime, getRuntimeDiff })

    render(<PluginsPage api={api} dashboard={runtimeChangesDashboard} />)

    fireEvent.click(screen.getByRole('button', { name: /Runtime Diff/ }))
    fireEvent.click(screen.getByLabelText('Select oxide/plugins/GatherManager.cs'))
    fireEvent.click(screen.getByLabelText('Select oxide/config/Old.json'))
    fireEvent.click(screen.getByRole('button', { name: 'Revert selected' }))
    expect(screen.getByRole('dialog', { name: 'Revert selected Runtime changes?' })).toBeVisible()
    fireEvent.click(screen.getByRole('button', { name: 'Confirm Revert' }))

    await waitFor(() => {
      expect(revertRuntime).toHaveBeenCalledWith({
        changes: [{ repository: 'configs', path: 'oxide/config/Economics.json' }],
      }, expect.any(AbortSignal))
    })
    expect(await screen.findByText('Succeeded')).toBeVisible()
    expect(getRuntimeDiff).toHaveBeenCalledTimes(1)
  })

  it('uses the bodyless Revert all operation after a destructive confirmation', async () => {
    const revertAllRuntime = vi.fn().mockResolvedValue({ operationId: 'op-runtime-revert-all' })
    const api = createPluginsApiScenario({ revertAllRuntime })

    render(<PluginsPage api={api} dashboard={runtimeChangesDashboard} />)

    fireEvent.click(screen.getByRole('button', { name: /Runtime Diff/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Revert all' }))
    expect(screen.getByRole('dialog', { name: 'Revert all Runtime changes?' })).toBeVisible()
    expect(screen.getByText(/discards all managed Runtime changes/)).toBeVisible()
    fireEvent.click(screen.getByRole('button', { name: 'Revert all' }))

    await waitFor(() => {
      expect(revertAllRuntime).toHaveBeenCalledWith(expect.any(AbortSignal))
    })
    expect(await screen.findByText('Succeeded')).toBeVisible()
  })

  it('does not offer Runtime Commit + Push in Production', () => {
    const dashboard = {
      ...productionPluginsDashboard,
      runtimeDiff: runtimeChangesDashboard.runtimeDiff,
    }
    render(<PluginsPage api={createPluginsApiScenario()} dashboard={dashboard} />)

    fireEvent.click(screen.getByRole('button', { name: /Runtime Diff/ }))

    expect(screen.queryByRole('button', { name: 'Commit selected + Push' })).not.toBeInTheDocument()
    expect(screen.getByText('Runtime Commit + Push is unavailable in Production.')).toBeVisible()
  })

  it('polls in-progress operations to a terminal report and blocks duplicate submission', async () => {
    const fetchGit = vi.fn().mockResolvedValue({ operationId: 'op-plugins-001' })
    const getOperation = vi
      .fn()
      .mockResolvedValueOnce({
        ...succeededPluginsOperation,
        status: 'inProgress',
        completedAt: null,
        currentPhase: 'applyRuntime',
      })
      .mockResolvedValueOnce(succeededPluginsOperation)
    const api = createPluginsApiScenario({ fetchGit, getOperation })

    render(<PluginsPage api={api} dashboard={stagingPluginsDashboard} />)

    openFetchConfirmation()
    const confirm = screen.getByRole('button', { name: 'Fetch Git' })
    fireEvent.click(confirm)
    fireEvent.click(confirm)

    expect(fetchGit).toHaveBeenCalledTimes(1)
    expect(await screen.findByRole('progressbar', { name: 'Operation progress' })).toHaveAttribute('aria-valuetext', 'In progress')
    expect(screen.getByText('Operation ID')).toBeVisible()
    expect(screen.getByText('op-plugins-001')).toBeVisible()

    expect(await screen.findByText('Succeeded', {}, { timeout: 2_000 })).toBeVisible()
    expect(getOperation).toHaveBeenCalledTimes(2)
  })

  it('shows backend failure and rollback information from a terminal report', async () => {
    const failedReport: GetPluginsOperationResponse = {
      ...succeededPluginsOperation,
      status: 'failed',
      failure: {
        phase: 'applyRuntime',
        code: 'RuntimeApplyFailed',
        message: 'A managed file could not be written.',
        repository: 'configs',
        filePath: 'oxide/config/Economics.json',
      },
      rollback: { status: 'succeeded', message: 'Runtime files were restored.' },
    }
    const api = createPluginsApiScenario({ getOperation: async () => failedReport })

    render(<PluginsPage api={api} dashboard={stagingPluginsDashboard} />)

    openFetchConfirmation()
    fireEvent.click(screen.getByRole('button', { name: 'Fetch Git' }))

    expect(await screen.findByText('RuntimeApplyFailed')).toBeVisible()
    expect(screen.getByText(/Runtime files were restored/)).toBeVisible()
  })

  it('keeps a successful operation report when independently refreshed data is unavailable', async () => {
    const getGitStatus = vi.fn().mockRejectedValue(new Error('Git status refresh is unavailable.'))
    const getReleases = vi.fn().mockResolvedValue(
      stagingPluginsDashboard.releases.status === 'available'
        ? stagingPluginsDashboard.releases.data
        : null,
    )
    const api = createPluginsApiScenario({ getGitStatus, getReleases })

    render(<PluginsPage api={api} dashboard={stagingPluginsDashboard} />)

    openFetchConfirmation()
    fireEvent.click(screen.getByRole('button', { name: 'Fetch Git' }))

    expect(await screen.findByText('Succeeded')).toBeVisible()
    expect(screen.getByText(/Operation report is final, but current screen data could not refresh/))
      .toHaveTextContent('Git status: Git status refresh is unavailable.')
    expect(screen.getByText('Git status unavailable: Git status refresh is unavailable.')).toBeVisible()
    expect(getGitStatus).toHaveBeenCalledTimes(1)
    expect(getReleases).toHaveBeenCalledTimes(1)
  })

  it('preserves an unknown operation outcome and retries only its status lookup', async () => {
    const fetchGit = vi.fn().mockResolvedValue({ operationId: 'op-plugins-unknown' })
    const getOperation = vi
      .fn()
      .mockRejectedValueOnce(new Error('Operation history is temporarily unavailable.'))
      .mockResolvedValueOnce({ ...succeededPluginsOperation, operationId: 'op-plugins-unknown' })
    const api = createPluginsApiScenario({ fetchGit, getOperation })

    render(<PluginsPage api={api} dashboard={stagingPluginsDashboard} />)

    openFetchConfirmation()
    fireEvent.click(screen.getByRole('button', { name: 'Fetch Git' }))

    expect(await screen.findByText('Status unavailable')).toBeVisible()
    expect(screen.getByText('op-plugins-unknown')).toBeVisible()
    expect(screen.getByRole('button', { name: '↻ Fetch Git' })).toBeDisabled()
    expect(screen.getByRole('button', { name: /Environment/ })).toBeDisabled()
    expect(screen.getByRole('button', { name: /Apply Source/ })).toBeDisabled()

    fireEvent.click(screen.getByRole('button', { name: 'Retry status' }))

    expect(await screen.findByText('Succeeded')).toBeVisible()
    expect(fetchGit).toHaveBeenCalledTimes(1)
    expect(getOperation).toHaveBeenCalledTimes(2)
  })

  it('keeps a reported mutation outcome unknown when its first status lookup fails', async () => {
    const fetchGit = vi.fn().mockRejectedValue(new HttpError({
      method: 'POST',
      url: '/plugins-api/api/git/fetch',
      status: 500,
      statusText: 'Internal Server Error',
      responseBody: {
        code: 'GitRemoteFetchFailed',
        message: 'The report was created before the fetch failed.',
        details: { operationId: 'op-plugins-reported' },
      },
    }))
    const getOperation = vi
      .fn()
      .mockRejectedValueOnce(new Error('Operation history is temporarily unavailable.'))
      .mockResolvedValueOnce({ ...succeededPluginsOperation, operationId: 'op-plugins-reported' })
    const api = createPluginsApiScenario({ fetchGit, getOperation })

    render(<PluginsPage api={api} dashboard={stagingPluginsDashboard} />)

    openFetchConfirmation()
    fireEvent.click(screen.getByRole('button', { name: 'Fetch Git' }))

    expect(await screen.findByText('Status unavailable')).toBeVisible()
    expect(screen.getByText('op-plugins-reported')).toBeVisible()

    fireEvent.click(screen.getByRole('button', { name: 'Retry status' }))

    expect(await screen.findByText('Succeeded')).toBeVisible()
    expect(fetchGit).toHaveBeenCalledTimes(1)
    expect(getOperation).toHaveBeenCalledTimes(2)
  })

  it('refreshes persistent operation history every time History is reopened', async () => {
    const getOperations = vi.fn().mockResolvedValue({ items: [], nextCursor: null })
    const api = createPluginsApiScenario({ getOperations })

    render(<PluginsPage api={api} dashboard={stagingPluginsDashboard} />)

    fireEvent.click(screen.getByRole('button', { name: '◷ History' }))
    expect(await screen.findByText('No operations have been recorded.')).toBeVisible()
    expect(getOperations).toHaveBeenNthCalledWith(1, 10, undefined, expect.any(AbortSignal))

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(screen.queryByRole('dialog', { name: 'Operation History' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: '◷ History' }))
    await waitFor(() => {
      expect(getOperations).toHaveBeenNthCalledWith(2, 10, undefined, expect.any(AbortSignal))
    })
  })
})
