import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { overviewScenarios } from '../mocks/overview-scenarios'
import { ActivityPanel } from './ActivityPanel'

describe('ActivityPanel', () => {
  it('renders no known operation when no operation detail is available', () => {
    render(
      <ActivityPanel
        acceptedOperation={null}
        operationDetail={overviewScenarios.ready.operationDetail}
      />,
    )

    expect(screen.getByText('No known operation.')).toBeVisible()
  })

  it('renders a running operation as active', () => {
    render(
      <ActivityPanel
        acceptedOperation={null}
        operationDetail={overviewScenarios.maintenance.operationDetail}
      />,
    )

    const panel = screen.getByRole('article', { name: 'Operation activity' })
    expect(panel).toHaveTextContent(/Type\s*Rust update/)
    expect(panel).toHaveTextContent(/Status\s*Running/)
    expect(panel).toHaveTextContent(/Created\s*2026-09-29T08:30:00Z/)
    expect(panel).toHaveTextContent(/Started\s*2026-09-29T08:30:02Z/)
  })

  it('renders accepted context while authoritative detail is loading', () => {
    render(
      <ActivityPanel
        acceptedOperation={{
          id: 'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
          status: 'Pending',
          target: 'oxide',
        }}
        operationDetail={{ status: 'loading' }}
      />,
    )

    const panel = screen.getByRole('article', { name: 'Operation activity' })
    expect(panel).toHaveTextContent('Oxide update accepted')
    expect(panel).toHaveTextContent(/Operation ID\s*f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4/)
    expect(panel).toHaveTextContent(/Accepted status\s*Pending/)
    expect(panel).toHaveTextContent('Waiting for authoritative operation detail.')
  })

  it('renders failed operation error and exit code', () => {
    render(
      <ActivityPanel
        acceptedOperation={null}
        operationDetail={overviewScenarios['update-failed'].operationDetail}
      />,
    )

    const panel = screen.getByRole('article', { name: 'Operation activity' })
    expect(panel).toHaveTextContent(/Status\s*Failed/)
    expect(panel).toHaveTextContent(/Finished\s*2026-09-29T07:11:07Z/)
    expect(panel).toHaveTextContent(/Exit code\s*1/)
    expect(panel).toHaveTextContent(/Error\s*SteamCMD exited with code 1\./)
  })

  it('makes an unknown operation type and status explicit', () => {
    render(
      <ActivityPanel
        acceptedOperation={null}
        operationDetail={{
          status: 'available',
          data: {
            id: 'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
            type: 'PluginUpdate',
            status: 'WaitingForApproval',
            createdAt: '2026-09-29T08:30:00Z',
            startedAt: null,
            finishedAt: null,
            exitCode: null,
            error: null,
          },
        }}
      />,
    )

    const panel = screen.getByRole('article', { name: 'Operation activity' })
    expect(panel).toHaveTextContent('Unknown operation')
    expect(panel).toHaveTextContent('Unknown status (WaitingForApproval)')
  })

  it('does not render optional fields absent from the API response', () => {
    render(
      <ActivityPanel
        acceptedOperation={null}
        operationDetail={{
          status: 'available',
          data: {
            id: 'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
            type: 'RustUpdate',
            status: 'Succeeded',
            createdAt: '2026-09-29T08:30:00Z',
          },
        }}
      />,
    )

    const panel = screen.getByRole('article', { name: 'Operation activity' })
    expect(panel).not.toHaveTextContent('Started')
    expect(panel).not.toHaveTextContent('Finished')
    expect(panel).not.toHaveTextContent('Exit code')
    expect(panel).not.toHaveTextContent('Error')
  })

  it('keeps accepted context when operation detail is unavailable', () => {
    render(
      <ActivityPanel
        acceptedOperation={{
          id: 'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
          status: 'Pending',
          target: 'rust',
        }}
        operationDetail={{ status: 'unavailable' }}
      />,
    )

    const panel = screen.getByRole('article', { name: 'Operation activity' })
    expect(panel).toHaveTextContent('Operation detail unavailable.')
    expect(panel).toHaveTextContent('Rust update')
    expect(panel).toHaveTextContent('f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4')
  })
})
