import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { overviewScenarios } from '../mocks/overview-scenarios'
import { deriveActionAvailability, type ControlPlaneInput } from '../model'
import { SoftwarePanel } from './SoftwarePanel'

function createPanelProps(overview: ControlPlaneInput) {
  return {
    actionAvailability: deriveActionAvailability(overview),
    actionsBlocked: false,
    onOxideUpdate: () => {},
    onRustUpdate: () => {},
    oxideUpdateError: null,
    oxideUpdateUnavailableReason: null,
    pendingUpdateTarget: null,
    rustUpdateError: null,
    rustUpdateUnavailableReason: null,
  }
}

describe('SoftwarePanel', () => {
  it('renders Rust installed and available Build IDs as distinct values', () => {
    const overview = overviewScenarios['rust-update-available']

    render(
      <SoftwarePanel
        {...createPanelProps(overview)}
        oxideVersions={overview.oxideVersions}
        rustVersions={overview.rustVersions}
      />,
    )

    const rustCard = screen.getByRole('article', {
      name: 'Rust version information',
    })
    expect(rustCard).toHaveTextContent(/Installed\s*12340000/)
    expect(rustCard).toHaveTextContent(/Available\s*12345678/)
    expect(rustCard).toHaveTextContent('Update available')
  })

  it('offers a Rust first-install operation on a clean host', () => {
    const stopped = overviewScenarios.stopped
    const overview: ControlPlaneInput = {
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
    const onRustUpdate = vi.fn()
    const onOxideUpdate = vi.fn()
    render(
      <SoftwarePanel
        {...createPanelProps(overview)}
        onRustUpdate={onRustUpdate}
        onOxideUpdate={onOxideUpdate}
        rustVersions={overview.rustVersions}
        oxideVersions={overview.oxideVersions}
      />,
    )
    expect(screen.getByRole('article', { name: 'Rust version information' })).toHaveTextContent(/Installed\s*Not installed/)
    expect(screen.getByRole('article', { name: 'Oxide version information' })).toHaveTextContent(/Installed\s*Not installed/)
    const installRust = screen.getByRole('button', { name: 'Install Rust' })
    const installOxide = screen.getByRole('button', { name: 'Install Oxide' })
    expect(installRust).toBeEnabled()
    expect(installOxide).toBeDisabled()
    expect(screen.getByText('Install Rust before installing Oxide.')).toBeVisible()
    fireEvent.click(installRust)
    expect(onRustUpdate).toHaveBeenCalledOnce()
    expect(onOxideUpdate).not.toHaveBeenCalled()
  })

  it('allows installing Oxide after Rust is installed', () => {
    const stopped = overviewScenarios.stopped
    const overview: ControlPlaneInput = {
      ...stopped,
      oxideVersions: {
        status: 'available',
        data: { ...stopped.oxideVersions.data, installedVersion: null, updateAvailable: true },
      },
    }
    const onOxideUpdate = vi.fn()
    render(
      <SoftwarePanel
        {...createPanelProps(overview)}
        onOxideUpdate={onOxideUpdate}
        rustVersions={overview.rustVersions}
        oxideVersions={overview.oxideVersions}
      />,
    )
    const installOxide = screen.getByRole('button', { name: 'Install Oxide' })
    expect(installOxide).toBeEnabled()
    fireEvent.click(installOxide)
    expect(onOxideUpdate).toHaveBeenCalledOnce()
  })

  it('renders Rust running metadata without labelling it as a Build ID', () => {
    const overview = overviewScenarios.ready

    render(
      <SoftwarePanel
        {...createPanelProps(overview)}
        oxideVersions={overview.oxideVersions}
        rustVersions={overview.rustVersions}
      />,
    )

    const rustCard = screen.getByRole('article', {
      name: 'Rust version information',
    })
    expect(rustCard).toHaveTextContent('Running metadata')
    expect(rustCard).toHaveTextContent(/Protocol\s*2633\.288\.1/)
    expect(rustCard).toHaveTextContent(/Build date\s*2026-09-22T07:48:10/)
    expect(rustCard).not.toHaveTextContent('Build ID')
  })

  it('treats omitted nullable running fields as not running', () => {
    const overview = overviewScenarios.stopped

    render(
      <SoftwarePanel
        {...createPanelProps(overview)}
        oxideVersions={{
          status: 'available',
          data: {
            installedVersion: '2.0.7725',
            availableVersion: '2.0.7726',
            updateAvailable: true,
          },
        }}
        rustVersions={{
          status: 'available',
          data: {
            branch: 'public',
            installedBuildId: 12345678,
            availableBuildId: 12345679,
            updateAvailable: true,
          },
        }}
      />,
    )

    expect(
      screen.getByRole('article', { name: 'Rust version information' }),
    ).toHaveTextContent(/Running\s*Not running/)
    expect(
      screen.getByRole('article', { name: 'Oxide version information' }),
    ).toHaveTextContent(/Running\s*Not running/)
    expect(screen.queryByText('Restart required')).not.toBeInTheDocument()
  })

  it('renders Oxide installed, running, and available versions separately', () => {
    const overview = overviewScenarios.ready

    render(
      <SoftwarePanel
        {...createPanelProps(overview)}
        oxideVersions={{
          status: 'available',
          data: {
            ...overview.oxideVersions.data,
            installedVersion: '2.0.7724',
            runningVersion: '2.0.7725',
            availableVersion: '2.0.7726',
          },
        }}
        rustVersions={overview.rustVersions}
      />,
    )

    const oxideCard = screen.getByRole('article', {
      name: 'Oxide version information',
    })
    expect(oxideCard).toHaveTextContent(/Installed\s*2\.0\.7724/)
    expect(oxideCard).toHaveTextContent(/Running\s*2\.0\.7725/)
    expect(oxideCard).toHaveTextContent(/Available\s*2\.0\.7726/)
  })

  it('shows restart required when the running Oxide version differs from installed', () => {
    const overview = overviewScenarios.ready

    render(
      <SoftwarePanel
        {...createPanelProps(overview)}
        oxideVersions={{
          status: 'available',
          data: {
            ...overview.oxideVersions.data,
            installedVersion: '2.0.7726',
            runningVersion: '2.0.7725',
          },
        }}
        rustVersions={overview.rustVersions}
      />,
    )

    expect(screen.getByText('Restart required')).toBeVisible()
  })

  it('keeps the available Oxide card visible when Rust is unavailable', () => {
    const overview = overviewScenarios['partial-unavailable']

    render(
      <SoftwarePanel
        {...createPanelProps(overview)}
        oxideVersions={overview.oxideVersions}
        rustVersions={overview.rustVersions}
      />,
    )

    expect(screen.getByText('Rust version information unavailable.')).toBeVisible()
    expect(
      screen.getByRole('article', { name: 'Oxide version information' }),
    ).toHaveTextContent(/Installed\s*2\.0\.7726/)
  })

  it('offers enabled, understandable update controls when stopped updates are eligible', () => {
    const rustOverview = overviewScenarios['rust-update-available']
    const oxideOverview = overviewScenarios['oxide-update-available']
    const onRustUpdate = vi.fn()
    const onOxideUpdate = vi.fn()

    render(
      <SoftwarePanel
        {...createPanelProps({
          ...rustOverview,
          oxideVersions: oxideOverview.oxideVersions,
        })}
        onOxideUpdate={onOxideUpdate}
        onRustUpdate={onRustUpdate}
        oxideVersions={oxideOverview.oxideVersions}
        rustVersions={rustOverview.rustVersions}
      />,
    )

    const rustButton = screen.getByRole('button', { name: 'Update to 12345678' })
    const oxideButton = screen.getByRole('button', { name: 'Update to 2.0.7726' })
    expect(rustButton).toBeEnabled()
    expect(oxideButton).toBeEnabled()

    fireEvent.click(rustButton)
    fireEvent.click(oxideButton)
    expect(onRustUpdate).toHaveBeenCalledOnce()
    expect(onOxideUpdate).toHaveBeenCalledOnce()
  })

  it('keeps an available update visible but disabled while the server is ready', () => {
    const overview = {
      ...overviewScenarios['rust-update-available'],
      serverStatus: overviewScenarios.ready.serverStatus,
    }

    render(
      <SoftwarePanel
        {...createPanelProps(overview)}
        rustUpdateUnavailableReason="Server must be stopped to update."
        rustVersions={overview.rustVersions}
        oxideVersions={overview.oxideVersions}
      />,
    )

    expect(screen.getByRole('button', { name: 'Update to 12345678' })).toBeDisabled()
    expect(screen.getByText('Server must be stopped to update.')).toBeVisible()
  })

  it('disables both update controls during another mutation and keeps a local error factual', () => {
    const rustOverview = overviewScenarios['rust-update-available']
    const oxideOverview = overviewScenarios['oxide-update-available']
    const onRustUpdate = vi.fn()
    const onOxideUpdate = vi.fn()
    const overview = {
      ...rustOverview,
      oxideVersions: oxideOverview.oxideVersions,
    }

    render(
      <SoftwarePanel
        {...createPanelProps(overview)}
        actionsBlocked
        onOxideUpdate={onOxideUpdate}
        onRustUpdate={onRustUpdate}
        oxideUpdateError="Oxide request failed."
        oxideVersions={oxideOverview.oxideVersions}
        pendingUpdateTarget="rust"
        rustUpdateError="Rust conflict."
        rustVersions={rustOverview.rustVersions}
      />,
    )

    const rustButton = screen.getByRole('button', {
      name: 'Requesting Rust update...',
    })
    const oxideButton = screen.getByRole('button', { name: 'Update to 2.0.7726' })
    expect(rustButton).toBeDisabled()
    expect(oxideButton).toBeDisabled()
    fireEvent.click(rustButton)
    fireEvent.click(oxideButton)
    expect(onRustUpdate).not.toHaveBeenCalled()
    expect(onOxideUpdate).not.toHaveBeenCalled()
    expect(screen.getByText('Rust conflict.')).toBeVisible()
    expect(
      screen.getByRole('article', { name: 'Rust version information' }),
    ).toHaveTextContent(/Installed\s*12340000/)
  })
})
