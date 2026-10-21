import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { overviewScenarios } from '../../overview/mocks/overview-scenarios'
import {
  deriveActionAvailability,
  type ActionAvailability,
  type LifecycleAction,
  type ControlPlaneInput,
} from '../../control-plane/model'
import { ServerRuntimePanel } from './ServerRuntimePanel'

interface ServerRuntimePanelOverrides {
  actionAvailability?: ActionAvailability
  lifecycleError?: string | null
  mutationsBlocked?: boolean
  pendingLifecycleAction?: LifecycleAction | null
}

function renderServerRuntimePanel(
  overview: ControlPlaneInput,
  {
    actionAvailability = deriveActionAvailability(overview),
    lifecycleError = null,
    pendingLifecycleAction = null,
    mutationsBlocked = pendingLifecycleAction !== null,
  }: ServerRuntimePanelOverrides = {},
) {
  const onStart = vi.fn()
  const onStop = vi.fn()
  const onRestart = vi.fn()

  render(
    <ServerRuntimePanel
      actionAvailability={actionAvailability}
      lifecycleError={lifecycleError}
      mutationsBlocked={mutationsBlocked}
      onRestart={onRestart}
      onStart={onStart}
      onStop={onStop}
      operationDetail={overview.operationDetail}
      pendingLifecycleAction={pendingLifecycleAction}
      serverStatus={overview.serverStatus}
      rustVersions={overview.rustVersions}
    />,
  )

  return { onRestart, onStart, onStop }
}

describe('ServerRuntimePanel', () => {
  it('renders a ready server with factual running and ready values', () => {
    const overview = overviewScenarios.ready

    renderServerRuntimePanel(overview)

    const panel = screen.getByRole('article', { name: 'Server state' })
    expect(panel).toHaveTextContent('Ready')
    expect(panel).toHaveTextContent(/Running\s*Yes/)
    expect(panel).toHaveTextContent(/Ready\s*Yes/)
    expect(panel).toHaveTextContent('Rust server is running and ready.')
  })

  it('enables Stop and Restart only when the server is ready', () => {
    const callbacks = renderServerRuntimePanel(overviewScenarios.ready)

    expect(screen.getByRole('button', { name: 'Start' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Stop' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Restart' })).toBeEnabled()

    fireEvent.click(screen.getByRole('button', { name: 'Stop' }))
    fireEvent.click(screen.getByRole('button', { name: 'Restart' }))

    expect(callbacks.onStop).toHaveBeenCalledOnce()
    expect(callbacks.onRestart).toHaveBeenCalledOnce()
  })

  it('enables Start only when the server is stopped', () => {
    const callbacks = renderServerRuntimePanel(overviewScenarios.stopped)

    expect(screen.getByRole('button', { name: 'Start' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Stop' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Restart' })).toBeDisabled()

    fireEvent.click(screen.getByRole('button', { name: 'Start' }))

    expect(callbacks.onStart).toHaveBeenCalledOnce()
  })

  it('keeps Start disabled until Rust is installed', () => {
    const callbacks = renderServerRuntimePanel({
      ...overviewScenarios.stopped,
      rustVersions: {
        status: 'available',
        data: {
          ...overviewScenarios.stopped.rustVersions.data,
          installedBuildId: null,
          updateAvailable: true,
        },
      },
    })
    expect(screen.getByText(/Rust is not installed/)).toBeVisible()
    expect(screen.getByRole('button', { name: 'Start' })).toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: 'Start' }))
    expect(callbacks.onStart).not.toHaveBeenCalled()
  })

  it('enables Stop only while the server is starting', () => {
    const callbacks = renderServerRuntimePanel(overviewScenarios.starting)

    expect(screen.getByRole('button', { name: 'Start' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Stop' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Restart' })).toBeDisabled()

    fireEvent.click(screen.getByRole('button', { name: 'Stop' }))

    expect(callbacks.onStop).toHaveBeenCalledOnce()
    expect(
      screen.getByText('Rust process is running. Waiting for server readiness.'),
    ).toBeVisible()
  })

  it('disables every lifecycle action and names the action while a mutation is pending', () => {
    renderServerRuntimePanel(overviewScenarios.ready, {
      pendingLifecycleAction: 'start',
    })

    expect(screen.getByRole('status')).toHaveTextContent('Starting...')
    expect(screen.getByRole('button', { name: 'Starting...' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Stop' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Restart' })).toBeDisabled()
  })

  it('shows a mutation error without hiding the factual server state', () => {
    renderServerRuntimePanel(overviewScenarios.ready, {
      lifecycleError: 'An update operation is already active.',
    })

    const panel = screen.getByRole('article', { name: 'Server state' })
    expect(screen.getByRole('alert')).toHaveTextContent(
      'An update operation is already active.',
    )
    expect(panel).toHaveTextContent('Ready')
    expect(panel).toHaveTextContent(/Running\s*Yes/)
    expect(panel).toHaveTextContent(/Ready\s*Yes/)
  })

  it('treats an inconsistent server response as locally unavailable', () => {
    renderServerRuntimePanel(overviewScenarios['inconsistent-status'])

    expect(screen.getByText('Inconsistent / Unavailable')).toBeVisible()
    expect(
      screen.getByText('Server reported an invalid running and ready combination.'),
    ).toBeVisible()
  })

  it('uses maintenance as the primary context for an active update', () => {
    renderServerRuntimePanel(overviewScenarios.maintenance)

    const panel = screen.getByRole('article', { name: 'Server state' })
    expect(panel).toHaveTextContent('Maintenance')
    expect(panel).toHaveTextContent('Rust update is running.')
    expect(panel).toHaveTextContent('Underlying server state: Stopped.')
  })

  it('renders a local unavailable state', () => {
    renderServerRuntimePanel({
      ...overviewScenarios.ready,
      serverStatus: {
        status: 'unavailable',
        message: 'Control service did not respond.',
      },
    })

    expect(screen.getByRole('alert')).toHaveTextContent('Server status unavailable.')
    expect(screen.getByText('Control service did not respond.')).toBeVisible()
  })
})
