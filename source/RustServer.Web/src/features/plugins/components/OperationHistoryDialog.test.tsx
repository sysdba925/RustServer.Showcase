import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import type { GetPluginsOperationResponse } from '../../../shared/api'
import { succeededPluginsOperation } from '../fixtures/plugins-api-scenarios'

import { OperationHistoryDialog } from './OperationHistoryDialog'

const firstPage = {
  items: [
    {
      operationId: 'op-interrupted',
      type: 'commitRuntime',
      status: 'interrupted',
      startedAt: '2026-10-10T10:15:00Z',
      completedAt: '2026-10-10T10:15:03Z',
      changedFiles: 2,
    },
    {
      operationId: 'op-success',
      type: 'fetchGit',
      status: 'succeeded',
      startedAt: '2026-10-10T10:10:00Z',
      completedAt: '2026-10-10T10:10:02Z',
      changedFiles: 0,
    },
  ],
  nextCursor: 'cursor-for-older-page',
}

const interruptedReport: GetPluginsOperationResponse = {
  ...succeededPluginsOperation,
  operationId: 'op-interrupted',
  type: 'commitRuntime',
  status: 'interrupted',
  completedAt: '2026-10-10T10:15:03Z',
  failure: {
    phase: 'captureFinalState',
    code: 'OperationInterrupted',
    message: 'The service restarted before the operation completed.',
    repository: null,
    filePath: null,
  },
}

describe('OperationHistoryDialog', () => {
  it('loads summaries, continues with the opaque cursor, and restarts when the page size changes', async () => {
    const getOperations = vi
      .fn()
      .mockResolvedValueOnce(firstPage)
      .mockResolvedValueOnce({
        items: [{
          operationId: 'op-older',
          type: 'revertRuntime',
          status: 'failed',
          startedAt: '2026-10-10T10:00:00Z',
          completedAt: '2026-10-10T10:00:01Z',
          changedFiles: 1,
        }],
        nextCursor: null,
      })
      .mockResolvedValueOnce({ items: [], nextCursor: null })
      .mockResolvedValueOnce({ items: [], nextCursor: null })

    render(
      <OperationHistoryDialog
        getOperation={vi.fn()}
        getOperations={getOperations}
        onClose={vi.fn()}
      />,
    )

    expect(screen.getByRole('status')).toHaveTextContent('Loading operation history')
    expect(await screen.findByText('op-interrupted')).toBeVisible()
    expect(screen.getByText('interrupted')).toBeVisible()
    expect(screen.getByText('2 changed files')).toBeVisible()
    expect(getOperations).toHaveBeenNthCalledWith(1, 10, undefined, expect.any(AbortSignal))

    fireEvent.click(screen.getByRole('button', { name: 'Load older operations' }))

    await waitFor(() => {
      expect(getOperations).toHaveBeenNthCalledWith(
        2,
        10,
        'cursor-for-older-page',
        expect.any(AbortSignal),
      )
    })
    expect(await screen.findByText('op-older')).toBeVisible()

    fireEvent.click(screen.getByRole('button', { name: '50' }))

    await waitFor(() => {
      expect(getOperations).toHaveBeenNthCalledWith(3, 50, undefined, expect.any(AbortSignal))
    })
    expect(await screen.findByText('No operations have been recorded.')).toBeVisible()
    expect(screen.queryByText('op-older')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: '200' }))

    await waitFor(() => {
      expect(getOperations).toHaveBeenNthCalledWith(4, 200, undefined, expect.any(AbortSignal))
    })
  })

  it('opens the selected persistent report and returns to the retained history list', async () => {
    const getOperation = vi.fn().mockResolvedValue(interruptedReport)

    render(
      <OperationHistoryDialog
        getOperation={getOperation}
        getOperations={vi.fn().mockResolvedValue(firstPage)}
        onClose={vi.fn()}
      />,
    )

    fireEvent.click(await screen.findByRole('button', { name: /op-interrupted/ }))

    await waitFor(() => {
      expect(getOperation).toHaveBeenCalledWith('op-interrupted', expect.any(AbortSignal))
    })
    expect(await screen.findByText('Interrupted')).toBeVisible()
    expect(screen.getByText('OperationInterrupted')).toBeVisible()

    fireEvent.click(screen.getByRole('button', { name: 'Back to History' }))

    expect(await screen.findByText('op-interrupted')).toBeVisible()
  })

  it('distinguishes initial loading, empty history, and a retryable history error', async () => {
    let resolveInitial: ((value: { items: []; nextCursor: null }) => void) | undefined
    const getOperations = vi
      .fn()
      .mockImplementationOnce(() => new Promise<{ items: []; nextCursor: null }>((resolve) => {
        resolveInitial = resolve
      }))
      .mockRejectedValueOnce(new Error('History storage is unavailable.'))
      .mockResolvedValueOnce({ items: [], nextCursor: null })

    render(
      <OperationHistoryDialog
        getOperation={vi.fn()}
        getOperations={getOperations}
        onClose={vi.fn()}
      />,
    )

    expect(screen.getByRole('status')).toHaveTextContent('Loading operation history')
    await waitFor(() => {
      expect(resolveInitial).toBeDefined()
    })
    await act(async () => {
      resolveInitial?.({ items: [], nextCursor: null })
    })
    expect(await screen.findByText('No operations have been recorded.')).toBeVisible()

    fireEvent.click(screen.getByRole('button', { name: '50' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('History storage is unavailable.')
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    expect(await screen.findByText('No operations have been recorded.')).toBeVisible()
  })
})
