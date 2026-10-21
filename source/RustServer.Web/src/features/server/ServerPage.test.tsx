import { fireEvent, render, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'

import { ControlPlaneContext } from '../control-plane/ControlPlaneContext'
import { createControlPlaneContextValue } from '../control-plane/fixtures/create-control-plane-context-value'
import { controlPlaneScenarios, startupCompleted } from '../control-plane/fixtures/control-plane-scenarios'
import { deriveActionAvailability, type ControlPlaneInput } from '../control-plane/model'
import { ServerPage } from './ServerPage'

function freshInstallInput(): ControlPlaneInput {
  const stopped = controlPlaneScenarios.stopped
  return {
    ...stopped,
    rustVersions: {
      status: 'available',
      data: { ...stopped.rustVersions.data, installedBuildId: null, updateAvailable: true },
    },
    oxideVersions: {
      status: 'available',
      data: { ...stopped.oxideVersions.data, installedVersion: null, updateAvailable: true },
    },
  }
}

describe('ServerPage v7 layout and behavior', () => {
  it('keeps lifecycle commands in one connected bar and renders real startup milestones', () => {
    const stop = vi.fn()
    const restart = vi.fn()
    render(
      <MemoryRouter>
        <ControlPlaneContext.Provider
          value={createControlPlaneContextValue({
            stop,
            restart,
            startupSnapshot: { status: 'available', data: startupCompleted },
          })}
        >
          <ServerPage />
        </ControlPlaneContext.Provider>
      </MemoryRouter>,
    )

    const bar = screen.getByRole('region', { name: 'Lifecycle controls' })
    expect(within(bar).getByRole('button', { name: 'Start' })).toBeDisabled()
    expect(within(bar).getByRole('button', { name: 'Stop' })).toBeEnabled()
    expect(within(bar).getByRole('button', { name: 'Restart' })).toBeEnabled()
    expect(within(bar).getByText('Container Running')).toBeVisible()
    expect(within(bar).getByText('Rust Ready')).toBeVisible()
    expect(within(bar).getByText('Available')).toBeVisible()
    expect(within(bar).getByRole('button', { name: /Network Access/i })).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Network Access' })).not.toBeInTheDocument()
    expect(screen.getByText('Startup Tracking')).toBeVisible()
    expect(screen.getByText('Steam server connection')).toBeVisible()
    expect(screen.getByText('NEXT START / RESTART')).toBeVisible()
    expect(screen.getByRole('article', { name: 'Rust version information' })).toHaveTextContent(/Installed\s*12345678/)
    expect(screen.getByRole('article', { name: 'Oxide version information' })).toHaveTextContent(/Installed\s*2\.0\.7726/)
    expect(screen.getByText('Running metadata')).toBeInTheDocument()

    fireEvent.click(within(bar).getByRole('button', { name: 'Stop' }))
    fireEvent.click(within(bar).getByRole('button', { name: 'Restart' }))
    expect(stop).toHaveBeenCalledOnce()
    expect(restart).toHaveBeenCalledOnce()
  })

  it('keeps real update actions inside Runtime and does not duplicate lifecycle buttons', () => {
    const state = controlPlaneScenarios['rust-update-available']
    const requestRustUpdate = vi.fn()
    render(
      <MemoryRouter>
        <ControlPlaneContext.Provider
          value={createControlPlaneContextValue({
            serverStatus: state.serverStatus,
            rustVersions: state.rustVersions,
            oxideVersions: state.oxideVersions,
            actionAvailability: deriveActionAvailability(state),
            requestRustUpdate,
          })}
        >
          <ServerPage />
        </ControlPlaneContext.Provider>
      </MemoryRouter>,
    )

    const runtime = screen.getByRole('region', { name: 'Runtime' })
    const rust = within(runtime).getByRole('article', { name: 'Rust version information' })
    const update = within(rust).getByRole('button', { name: 'Update to 12345678' })
    expect(update).toBeEnabled()
    expect(screen.getAllByRole('button', { name: 'Start' })).toHaveLength(1)
    fireEvent.click(update)
    expect(requestRustUpdate).toHaveBeenCalledOnce()
  })

  it('does not misrepresent unknown startup availability as a confirmed connection', () => {
    render(
      <MemoryRouter>
        <ControlPlaneContext.Provider value={createControlPlaneContextValue({
          startupSnapshot: { status: 'unavailable', message: 'Startup telemetry unavailable.' },
        })}>
          <ServerPage />
        </ControlPlaneContext.Provider>
      </MemoryRouter>,
    )
    const bar = screen.getByRole('region', { name: 'Lifecycle controls' })
    expect(within(bar).getByText('Availability Unknown')).toBeVisible()
    expect(screen.getByText('Startup telemetry unavailable.')).toBeVisible()
  })
})

describe('ServerPage inconsistent server status', () => {
  it('shows an inconsistent control status without a misleading Rust Ready indicator', () => {
    const inconsistent = controlPlaneScenarios['inconsistent-status']
    render(
      <MemoryRouter>
        <ControlPlaneContext.Provider value={createControlPlaneContextValue({
          serverStatus: inconsistent.serverStatus,
          rustVersions: inconsistent.rustVersions,
          oxideVersions: inconsistent.oxideVersions,
          actionAvailability: deriveActionAvailability(inconsistent),
          startupSnapshot: { status: 'available', data: startupCompleted },
        })}>
          <ServerPage />
        </ControlPlaneContext.Provider>
      </MemoryRouter>,
    )

    const bar = screen.getByRole('region', { name: 'Lifecycle controls' })
    expect(within(bar).getByText('Rust Unknown')).toBeVisible()
    expect(within(bar).getByText('Not Available')).toBeVisible()
    expect(within(bar).getByRole('button', { name: 'Start' })).toBeDisabled()
    expect(screen.getByText('Server reported an invalid running and ready combination.')).toBeVisible()
  })
})

describe('ServerPage installation', () => {
  it('offers first-install Rust from the Server page and prevents premature Start', () => {
    const fresh = freshInstallInput()
    const requestRustUpdate = vi.fn()
    render(
      <MemoryRouter>
        <ControlPlaneContext.Provider
          value={createControlPlaneContextValue({
            serverStatus: fresh.serverStatus,
            rustVersions: fresh.rustVersions,
            oxideVersions: fresh.oxideVersions,
            actionAvailability: deriveActionAvailability(fresh),
            requestRustUpdate,
          })}
        >
          <ServerPage />
        </ControlPlaneContext.Provider>
      </MemoryRouter>,
    )

    const installRust = screen.getByRole('button', { name: 'Install Rust' })
    expect(installRust).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Install Oxide' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Start' })).toBeDisabled()
    fireEvent.click(installRust)
    expect(requestRustUpdate).toHaveBeenCalledOnce()
  })

  it('shows the accepted operation on the same page', () => {
    const fresh = freshInstallInput()
    const operationId = 'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4'
    render(
      <MemoryRouter>
        <ControlPlaneContext.Provider
          value={createControlPlaneContextValue({
            serverStatus: fresh.serverStatus,
            rustVersions: fresh.rustVersions,
            oxideVersions: fresh.oxideVersions,
            actionAvailability: deriveActionAvailability(fresh),
            acceptedOperation: { id: operationId, status: 'Pending', target: 'rust' },
            operationDetail: { status: 'loading' },
          })}
        >
          <ServerPage />
        </ControlPlaneContext.Provider>
      </MemoryRouter>,
    )

    expect(screen.getByRole('article', { name: 'Operation activity' })).toHaveTextContent(
      operationId,
    )
    expect(screen.getByText('Waiting for authoritative operation detail.')).toBeVisible()
  })
})
