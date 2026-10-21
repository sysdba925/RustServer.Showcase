import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const api = vi.hoisted(() => ({
  getSnapshots: vi.fn(),
  createSnapshot: vi.fn(),
  restoreSnapshot: vi.fn(),
  deleteSnapshot: vi.fn(),
  getSnapshotOperation: vi.fn(),
  HttpError: class HttpError extends Error {
    readonly status: number
    readonly problemDetails: { title?: string | null; detail?: string | null }

    constructor(status: number, detail?: string) {
      super(`HTTP ${status}`)
      this.status = status
      this.problemDetails = detail ? { detail } : {}
    }
  },
}))

vi.mock('../../../shared/api', () => api)

import { SnapshotsPanel } from './SnapshotsPanel'

const snapshot = {
  id: 'snapshot-one',
  createdAt: '2026-10-07T14:35:00Z',
  type: 'consistent' as const,
  sizeBytes: Math.round(37.8 * 1024 * 1024),
}

const stoppedServer = {
  status: 'available' as const,
  data: { isRunning: false, isReady: false },
}

function renderSnapshotsPanel(
  serverStatus: typeof stoppedServer = stoppedServer,
) {
  return render(<SnapshotsPanel serverStatus={serverStatus} />)
}

beforeEach(() => {
  vi.clearAllMocks()
  api.getSnapshots.mockResolvedValue([])
})

afterEach(() => {
  vi.useRealTimers()
})

describe('SnapshotsPanel', () => {
  it('renders snapshot summaries without exposing opaque IDs', async () => {
    api.getSnapshots.mockResolvedValue([snapshot])

    renderSnapshotsPanel()

    expect(await screen.findByRole('columnheader', { name: 'Created' })).toBeVisible()
    expect(screen.getByText('Consistent')).toBeVisible()
    expect(screen.getByText('37.8 MB')).toBeVisible()
    expect(screen.queryByText(snapshot.id)).not.toBeInTheDocument()
  })

  it('polls a create operation and refreshes the list after success', async () => {
    let resolveOperation: ((response: unknown) => void) | undefined
    api.getSnapshots
      .mockResolvedValueOnce([])
      .mockResolvedValueOnce([{ ...snapshot, type: 'fast' as const }])
    api.createSnapshot.mockResolvedValue({ operationId: 'create-1', status: 'pending' })
    api.getSnapshotOperation.mockImplementation(
      () =>
        new Promise((resolve) => {
          resolveOperation = resolve
        }),
    )

    renderSnapshotsPanel()
    await screen.findByText('No snapshots yet.')

    fireEvent.click(screen.getByRole('button', { name: 'Create Fast Snapshot' }))

    expect(await screen.findByText('Creating fast snapshot...')).toBeVisible()
    await waitFor(() => expect(api.getSnapshotOperation).toHaveBeenCalledTimes(1))

    resolveOperation?.({
      operationId: 'create-1',
      type: 'createSnapshot',
      status: 'succeeded',
      createdAt: snapshot.createdAt,
      startedAt: snapshot.createdAt,
      finishedAt: snapshot.createdAt,
      error: null,
    })

    expect(await screen.findByText('Fast')).toBeVisible()
    expect(api.getSnapshots).toHaveBeenCalledTimes(2)
  })

  it('surfaces the stopped-server requirement returned for a consistent snapshot', async () => {
    api.createSnapshot.mockRejectedValue(new api.HttpError(409, 'ServerMustBeStopped'))

    renderSnapshotsPanel()
    await screen.findByText('No snapshots yet.')
    fireEvent.click(
      screen.getByRole('button', { name: 'Create Consistent Snapshot' }),
    )

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Stop the Rust server before creating a consistent snapshot.',
    )
  })

  it('requires confirmation before restoring the selected snapshot', async () => {
    api.getSnapshots.mockResolvedValue([snapshot])
    api.restoreSnapshot.mockResolvedValue({
      operationId: 'restore-1',
      status: 'pending',
    })
    api.getSnapshotOperation.mockResolvedValue({
      operationId: 'restore-1',
      type: 'restoreSnapshot',
      status: 'succeeded',
      createdAt: snapshot.createdAt,
      startedAt: snapshot.createdAt,
      finishedAt: snapshot.createdAt,
      error: null,
    })

    renderSnapshotsPanel()
    await screen.findByText('Consistent')
    fireEvent.click(screen.getByRole('button', { name: 'Restore' }))

    const confirmation = screen.getByRole('dialog', { name: 'Restore snapshot' })
    expect(confirmation).toHaveTextContent('Current Rust server state will be replaced.')
    expect(confirmation).toHaveTextContent('The server must be stopped before restore.')

    fireEvent.click(screen.getByRole('button', { name: 'Restore snapshot' }))

    await waitFor(() =>
      expect(api.restoreSnapshot).toHaveBeenCalledWith(
        snapshot.id,
        expect.any(AbortSignal),
      ),
    )
  })

  it('explains a delete conflict after confirmation', async () => {
    api.getSnapshots.mockResolvedValue([snapshot])
    api.deleteSnapshot.mockRejectedValue(new api.HttpError(409))

    renderSnapshotsPanel()
    await screen.findByText('Consistent')
    fireEvent.click(screen.getByRole('button', { name: 'Delete' }))

    expect(screen.getByRole('dialog', { name: 'Delete snapshot' })).toBeVisible()
    fireEvent.click(screen.getByRole('button', { name: 'Delete snapshot' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'A snapshot operation is in progress. Try again when it finishes.',
    )
  })

  it('shows the real stored count and refreshes snapshots from the World toolbar', async () => {
    api.getSnapshots.mockResolvedValueOnce([snapshot]).mockResolvedValueOnce([])
    render(<SnapshotsPanel compact serverStatus={stoppedServer} />)

    expect(await screen.findByText('1 stored')).toBeVisible()
    fireEvent.click(screen.getByRole('button', { name: /Refresh/i }))

    expect(await screen.findByText('0 stored')).toBeVisible()
    expect(api.getSnapshots).toHaveBeenCalledTimes(2)
    expect(screen.getByText('No snapshots yet.')).toBeVisible()
  })

  it('keeps stopped-server guards in the V1 snapshot layout', async () => {
    api.getSnapshots.mockResolvedValue([snapshot])
    render(
      <SnapshotsPanel
        compact
        serverStatus={{ status: 'available', data: { isRunning: true, isReady: true } }}
      />,
    )
    expect(await screen.findByText('1 stored')).toBeVisible()
    expect(screen.getByRole('button', { name: 'Create Fast Snapshot' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Create Consistent Snapshot' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Restore' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Delete' })).toBeEnabled()
  })

})
