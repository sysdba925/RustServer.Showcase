import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'

import { ControlPlaneContext } from '../control-plane/ControlPlaneContext'
import { createControlPlaneContextValue } from '../control-plane/fixtures/create-control-plane-context-value'
import {
  controlPlaneScenarios,
  startupInProgress,
} from '../control-plane/fixtures/control-plane-scenarios'
import { deriveActionAvailability } from '../control-plane/model'
import { OverviewPage } from './OverviewPage'

function renderOverview(
  overrides: Parameters<typeof createControlPlaneContextValue>[0] = {},
) {
  return render(
    <MemoryRouter basename="/dashboard" initialEntries={['/dashboard/']}>
      <ControlPlaneContext.Provider value={createControlPlaneContextValue(overrides)}>
        <OverviewPage />
      </ControlPlaneContext.Provider>
    </MemoryRouter>,
  )
}

describe('Overview V5', () => {
  it('renders the three read-only regions, software details and a Server shortcut', () => {
    renderOverview()
    expect(screen.getByRole('heading', { name: 'Overview' })).toBeVisible()
    expect(screen.getByRole('region', { name: 'Server' })).toBeVisible()
    expect(screen.getByRole('region', { name: 'Software' })).toBeVisible()
    expect(screen.getByRole('region', { name: 'Activity' })).toBeVisible()
    expect(screen.getByRole('link', { name: 'Manage server' })).toHaveAttribute(
      'href', '/dashboard/server',
    )
    expect(screen.getByRole('article', { name: 'Server summary' })).toHaveTextContent('Ready')
    expect(screen.getByRole('article', { name: 'Rust version information' })).toHaveTextContent(/Installed\s*12345678/)
    expect(screen.getByRole('article', { name: 'Oxide version information' })).toHaveTextContent(/Installed\s*2\.0\.7726/)
    expect(screen.getByText('Running metadata')).toBeInTheDocument()
    expect(screen.getByRole('article', { name: 'Operation activity' })).toHaveTextContent('No known operation.')
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('reports updates without showing Install or Update buttons or requesting mutations', () => {
    const rust = controlPlaneScenarios['rust-update-available']
    const oxide = controlPlaneScenarios['oxide-update-available']
    const onRust = vi.fn()
    const onOxide = vi.fn()
    renderOverview({
      serverStatus: rust.serverStatus,
      rustVersions: rust.rustVersions,
      oxideVersions: oxide.oxideVersions,
      operationDetail: rust.operationDetail,
      actionAvailability: deriveActionAvailability(rust),
      requestRustUpdate: onRust,
      requestOxideUpdate: onOxide,
    })
    const software = screen.getByRole('region', { name: 'Software' })
    expect(within(software).getAllByText('Update available')).toHaveLength(2)
    expect(within(software).getByRole('article', { name: 'Rust version information' })).toHaveTextContent('12340000')
    expect(within(software).getByRole('article', { name: 'Oxide version information' })).toHaveTextContent('2.0.7725')
    expect(within(software).queryByRole('button')).not.toBeInTheDocument()
    expect(onRust).not.toHaveBeenCalled()
    expect(onOxide).not.toHaveBeenCalled()
  })

  it('shows resolved startup milestone count only during Starting', () => {
    const starting = controlPlaneScenarios.starting
    renderOverview({
      serverStatus: starting.serverStatus,
      rustVersions: starting.rustVersions,
      oxideVersions: starting.oxideVersions,
      startupSnapshot: { status: 'available', data: startupInProgress },
    })
    const server = screen.getByRole('article', { name: 'Server summary' })
    expect(server).toHaveTextContent('Starting')
    expect(server).toHaveTextContent('Startup: 8 / 9 milestones')
    expect(server).not.toHaveTextContent('Steam server connection')
  })

  it('keeps Oxide available if Rust versions fail, with per-resource error states', () => {
    const partial = controlPlaneScenarios['partial-unavailable']
    renderOverview({
      serverStatus: partial.serverStatus,
      rustVersions: partial.rustVersions,
      oxideVersions: partial.oxideVersions,
    })
    const rust = screen.getByRole('article', { name: 'Rust version information' })
    const oxide = screen.getByRole('article', { name: 'Oxide version information' })
    expect(within(rust).getByRole('alert')).toHaveTextContent('Rust version information unavailable.')
    expect(rust).toHaveTextContent('Rust version service did not respond.')
    expect(oxide).toHaveTextContent(/Installed\s*2\.0\.7726/)
  })

  it('handles optional running version fields without rendering fake running metadata', () => {
    renderOverview({
      rustVersions: {
        status: 'available',
        data: {
          branch: 'public',
          installedBuildId: 12345678,
          availableBuildId: 12345678,
          updateAvailable: false,
        },
      },
      oxideVersions: {
        status: 'available',
        data: {
          installedVersion: '2.0.7830',
          availableVersion: '2.0.7830',
          updateAvailable: false,
        },
      },
    })
    const rust = screen.getByRole('article', { name: 'Rust version information' })
    const oxide = screen.getByRole('article', { name: 'Oxide version information' })
    expect(rust).toHaveTextContent(/Running\s*Not running/)
    expect(rust).not.toHaveTextContent('Running metadata')
    expect(oxide).toHaveTextContent(/Running\s*Not running/)
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('preserves operation details and errors from the original dashboard', () => {
    const failing = controlPlaneScenarios['update-failed']
    renderOverview({
      operationDetail: failing.operationDetail,
    })
    const activity = screen.getByRole('article', { name: 'Operation activity' })
    expect(activity).toHaveTextContent(/Status\s*Failed/)
    expect(activity).toHaveTextContent(/Exit code\s*1/)
    expect(activity).toHaveTextContent('SteamCMD exited with code 1.')
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('keeps accepted operation context while authoritative details are loading', () => {
    renderOverview({
      acceptedOperation: {
        id: 'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
        target: 'oxide',
        status: 'Pending',
      },
      operationDetail: { status: 'loading' },
    })
    const activity = screen.getByRole('article', { name: 'Operation activity' })
    expect(activity).toHaveTextContent('Oxide update accepted')
    expect(activity).toHaveTextContent('f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4')
    expect(activity).toHaveTextContent('Waiting for authoritative operation detail.')
  })
})
