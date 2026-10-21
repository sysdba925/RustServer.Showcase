import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { PluginsRuntimeChangesPage } from './PluginsRuntimeChangesPage'
import { RuntimeDiffPanel } from './components/RuntimeDiffPanel'

const api = vi.hoisted(() => ({
  HttpError: class HttpError extends Error {
    status: number
    problemDetails: { detail?: string; title?: string } | undefined
    constructor(status: number, detail?: string) { super(`HTTP ${status}`); this.status = status; this.problemDetails = detail ? { detail } : undefined }
  },
  getStagingRuntimeChanges: vi.fn(),
  getStagingRuntimeChangeDiff: vi.fn(),
  importStagingRuntimeChanges: vi.fn(),
  discardStagingRuntimeChanges: vi.fn(),
}))

vi.mock('../../shared/api', () => api)

const comparison = {
  source: 'development', pluginsSha: 'plugins-sha-1234567890', oxideConfigsSha: 'config-sha-1234567890',
  changes: [
    { path: 'oxide/plugins/Added.cs', repository: 'plugins' as const, changeType: 'added' as const, textDiffAvailable: true, changeToken: 'added-token' },
    { path: 'oxide/config/Modified.json', repository: 'oxideConfigs' as const, changeType: 'modified' as const, textDiffAvailable: true, changeToken: 'modified-token' },
    { path: 'oxide/config/Deleted.json', repository: 'oxideConfigs' as const, changeType: 'deleted' as const, textDiffAvailable: false, changeToken: 'deleted-token' },
  ],
}

function renderPage() {
  const router = createMemoryRouter([{ path: '/plugins/runtime-changes', element: <PluginsRuntimeChangesPage /> }], { initialEntries: ['/plugins/runtime-changes'] })
  return render(<RouterProvider router={router} />)
}

beforeEach(() => {
  vi.clearAllMocks()
  api.getStagingRuntimeChanges.mockResolvedValue(comparison)
  api.getStagingRuntimeChangeDiff.mockResolvedValue({ path: 'oxide/plugins/Added.cs', changeType: 'added', baselineText: null, runtimeText: 'class Added {}' })
  api.importStagingRuntimeChanges.mockResolvedValue({ commits: [{ repository: 'plugins', previousHead: 'old', commitSha: 'new-plugin-sha', committedPaths: ['oxide/plugins/Added.cs'] }, { repository: 'oxideConfigs', previousHead: 'old-config', commitSha: 'new-config-sha', committedPaths: ['oxide/config/Modified.json'] }] })
  api.discardStagingRuntimeChanges.mockResolvedValue({ discardedPaths: ['oxide/plugins/Added.cs'] })
})

describe('Plugins Runtime Changes', () => {
  it('loads the exact baseline, all change types, and clears selection on refresh', async () => {
    renderPage()
    expect(await screen.findByText('Development')).toBeVisible()
    expect(screen.getByText('plugins-sha-…')).toBeVisible()
    expect(screen.getByText('added')).toBeVisible()
    expect(screen.getByText('modified')).toBeVisible()
    expect(screen.getByText('deleted')).toBeVisible()
    fireEvent.click(screen.getByLabelText('Select oxide/plugins/Added.cs'))
    expect(screen.getByLabelText('Select oxide/plugins/Added.cs')).toBeChecked()
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))
    await waitFor(() => expect(api.getStagingRuntimeChanges).toHaveBeenCalledTimes(2))
    expect(screen.getByLabelText('Select oxide/plugins/Added.cs')).not.toBeChecked()
  })

  it('uses the exact path for a diff and correctly labels nullable added/deleted sides', async () => {
    renderPage()
    fireEvent.click((await screen.findAllByRole('button', { name: 'View diff' }))[0])
    expect(await screen.findByRole('heading', { name: 'Runtime file diff' })).toBeVisible()
    expect(api.getStagingRuntimeChangeDiff).toHaveBeenCalledWith('oxide/plugins/Added.cs')
    expect(screen.getByText('Deployed baseline: no file')).toBeVisible()
    expect(screen.getByText('Runtime')).toBeVisible()
    expect(screen.getByText('Text diff unavailable')).toBeVisible()
  })

  it('labels a nullable runtime side as deleted', () => {
    render(<RuntimeDiffPanel diff={{ path: 'oxide/config/Deleted.json', changeType: 'deleted', baselineText: '{"enabled":true}', runtimeText: null }} error={null} loading={false} onClose={() => undefined} />)

    expect(screen.getByText('Deployed baseline')).toBeVisible()
    expect(screen.getByText('Runtime: file deleted')).toBeVisible()
  })

  it('imports selected path/token pairs as one logical operation and leaves the baseline unchanged', async () => {
    renderPage()
    fireEvent.click(await screen.findByLabelText('Select oxide/plugins/Added.cs'))
    fireEvent.click(screen.getByLabelText('Select oxide/config/Modified.json'))
    fireEvent.click(screen.getByRole('button', { name: 'Review Import' }))
    fireEvent.change(screen.getByLabelText('Runtime import commit message'), { target: { value: 'Accept runtime state' } })
    fireEvent.click(screen.getByRole('button', { name: 'Import Runtime Changes' }))
    await waitFor(() => expect(api.importStagingRuntimeChanges).toHaveBeenCalledWith({ message: 'Accept runtime state', changes: [{ path: 'oxide/plugins/Added.cs', expectedChangeToken: 'added-token' }, { path: 'oxide/config/Modified.json', expectedChangeToken: 'modified-token' }] }))
    expect(await screen.findByText(/The Staging deployment baseline has not changed/)).toBeVisible()
    expect(api.discardStagingRuntimeChanges).not.toHaveBeenCalled()
    expect(api.getStagingRuntimeChanges).toHaveBeenCalledTimes(2)
    expect(screen.getByText('new-plugin-sha')).toBeVisible()
    expect(screen.getByText('new-config-sha')).toBeVisible()
  })

  it('does not offer import for release or unknown sources', async () => {
    api.getStagingRuntimeChanges.mockResolvedValueOnce({ ...comparison, source: 'release' })
    renderPage()
    const releaseImport = await screen.findByRole('button', { name: 'Review Import' })
    expect(releaseImport).toBeDisabled()
    fireEvent.click(screen.getByLabelText('Select oxide/plugins/Added.cs'))
    expect(screen.getByRole('button', { name: 'Review Discard' })).toBeEnabled()
    expect(screen.getByText('Runtime changes from a Release baseline cannot be imported into Development.')).toBeVisible()
    api.getStagingRuntimeChanges.mockResolvedValueOnce({ ...comparison, source: 'future-source' })
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))
    await screen.findByText('<unknown source>')
    expect(screen.getByRole('button', { name: 'Review Import' })).toBeDisabled()
    expect(screen.getByText('Import is unavailable because this runtime baseline source is not supported by the published workflow.')).toBeVisible()
  })

  it('requires destructive confirmation and discards only selected path/token pairs', async () => {
    renderPage()
    fireEvent.click(await screen.findByLabelText('Select oxide/plugins/Added.cs'))
    expect(api.discardStagingRuntimeChanges).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Review Discard' }))
    expect(screen.getByRole('heading', { name: 'Discard selected runtime changes?' })).toBeVisible()
    fireEvent.click(screen.getByRole('button', { name: 'Discard Runtime Changes' }))
    await waitFor(() => expect(api.discardStagingRuntimeChanges).toHaveBeenCalledWith({ changes: [{ path: 'oxide/plugins/Added.cs', expectedChangeToken: 'added-token' }] }))
    expect(await screen.findByText(/Discarded 1 runtime change/)).toBeVisible()
    expect(api.importStagingRuntimeChanges).not.toHaveBeenCalled()
  })

  it('shows empty, baseline conflict, and Staging-unavailable states without treating them as the same state', async () => {
    api.getStagingRuntimeChanges.mockResolvedValueOnce({ ...comparison, changes: [] })
    renderPage()
    expect(await screen.findByText('No managed runtime changes detected.')).toBeVisible()
    api.getStagingRuntimeChanges.mockRejectedValueOnce(new api.HttpError(409, 'Deployment manifest is invalid.'))
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Runtime changes conflict: Deployment manifest is invalid.')
    api.getStagingRuntimeChanges.mockRejectedValueOnce(new api.HttpError(403, 'Current context is Production.'))
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Runtime Changes are available only for the permitted Staging context. Current context is Production.')
  })

  it('refreshes and clears stale selection, diff, and confirmation after an import conflict', async () => {
    api.importStagingRuntimeChanges.mockRejectedValueOnce(new api.HttpError(409, 'Token stale.'))
    renderPage()
    fireEvent.click(await screen.findByLabelText('Select oxide/plugins/Added.cs'))
    fireEvent.click(screen.getAllByRole('button', { name: 'View diff' })[0])
    await screen.findByRole('heading', { name: 'Runtime file diff' })
    fireEvent.click(screen.getByRole('button', { name: 'Review Import' }))
    fireEvent.click(screen.getByRole('button', { name: 'Import Runtime Changes' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Runtime state changed since it was reviewed.')
    expect(screen.getByLabelText('Select oxide/plugins/Added.cs')).not.toBeChecked()
    expect(screen.queryByRole('heading', { name: 'Runtime file diff' })).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Import Runtime Changes into Development' })).not.toBeInTheDocument()
  })

  it('refreshes and clears stale selection after a discard conflict', async () => {
    api.discardStagingRuntimeChanges.mockRejectedValueOnce(new api.HttpError(409, 'Token stale.'))
    renderPage()
    fireEvent.click(await screen.findByLabelText('Select oxide/plugins/Added.cs'))
    fireEvent.click(screen.getByRole('button', { name: 'Review Discard' }))
    fireEvent.click(screen.getByRole('button', { name: 'Discard Runtime Changes' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Runtime state changed since it was reviewed.')
    expect(screen.getByLabelText('Select oxide/plugins/Added.cs')).not.toBeChecked()
    expect(screen.queryByRole('heading', { name: 'Discard selected runtime changes?' })).not.toBeInTheDocument()
  })

  it('does not request an unavailable text diff and refreshes stale diff state after a diff conflict', async () => {
    api.getStagingRuntimeChangeDiff.mockRejectedValueOnce(new api.HttpError(409, 'Runtime changed.'))
    renderPage()
    fireEvent.click(await screen.findByLabelText('Select oxide/plugins/Added.cs'))
    fireEvent.click(screen.getAllByRole('button', { name: 'View diff' })[0])
    expect(await screen.findByRole('alert')).toHaveTextContent('Runtime state changed since it was reviewed.')
    expect(screen.getByLabelText('Select oxide/plugins/Added.cs')).not.toBeChecked()
    expect(screen.queryByRole('heading', { name: 'Runtime file diff' })).not.toBeInTheDocument()
    expect(api.getStagingRuntimeChangeDiff).toHaveBeenCalledTimes(1)
    expect(screen.getByText('Text diff unavailable')).toBeVisible()
  })
})
