import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { StartupTracking } from './StartupTracking'

const schema = {
  status: 'available' as const,
  data: {
    version: 1,
    milestones: [
      { id: 'engine', label: 'Unity Engine' },
      { id: 'mono', label: 'Mono Runtime' },
      { id: 'oxide', label: 'Oxide Core' },
    ],
  },
}

describe('StartupTracking', () => {
  it('uses backend labels and renders reached, skipped, and pending states', () => {
    render(
      <StartupTracking
        startupSchema={schema}
        startupSnapshot={{
          status: 'available',
          data: {
            sessionId: 'stp_run-a',
            started: false,
            milestones: ['reached', 'skipped', 'pending'],
          },
        }}
      />,
    )

    const tracking = screen.getByRole('region', { name: 'Startup progress' })
    expect(tracking).toHaveTextContent('Unity Engine')
    expect(tracking).toHaveTextContent('Mono Runtime')
    expect(tracking).toHaveTextContent('Oxide Core')
    expect(tracking).toHaveTextContent('Reached')
    expect(tracking).toHaveTextContent('Skipped')
    expect(tracking).toHaveTextContent('Pending')
    expect(tracking).toHaveTextContent('Startup milestones are being observed.')
    expect(tracking).not.toHaveTextContent('failed')
  })

  it('uses the backend started value without deriving completion from milestones', () => {
    render(
      <StartupTracking
        startupSchema={schema}
        startupSnapshot={{
          status: 'available',
          data: {
            sessionId: 'stp_run-a',
            started: true,
            milestones: ['reached', 'pending', 'pending'],
          },
        }}
      />,
    )

    expect(screen.getByText('Startup milestones complete.')).toBeVisible()
  })

  it('does not render milestones from an earlier session after a stopped snapshot', () => {
    render(
      <StartupTracking
        startupSchema={schema}
        startupSnapshot={{
          status: 'available',
          data: {
            sessionId: null,
            started: false,
            milestones: [],
          },
        }}
      />,
    )

    expect(screen.getByText('No active startup session.')).toBeVisible()
    expect(screen.queryByRole('list', { name: 'Startup milestones' })).not.toBeInTheDocument()
  })

  it('keeps startup resource failures local', () => {
    render(
      <StartupTracking
        startupSchema={{ status: 'unavailable', message: 'Schema request failed.' }}
        startupSnapshot={{ status: 'available', data: { sessionId: null, started: false, milestones: [] } }}
      />,
    )

    expect(screen.getByRole('alert')).toHaveTextContent('Startup progress unavailable.')
    expect(screen.getByText('Schema request failed.')).toBeVisible()
  })
})
