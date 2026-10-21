import { render, screen } from '@testing-library/react'
import type { ComponentProps } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'

import { ServerSummaryPanel } from './ServerSummaryPanel'

const startupSchema = {
  status: 'available' as const,
  data: {
    version: 1,
    milestones: [
      { id: 'engine', label: 'Unity Engine' },
      { id: 'mono', label: 'Mono Runtime' },
      { id: 'world', label: 'World loading' },
    ],
  },
}

function renderSummary(
  overrides: Partial<ComponentProps<typeof ServerSummaryPanel>> = {},
) {
  return render(
    <MemoryRouter basename="/dashboard" initialEntries={['/dashboard/']}>
      <ServerSummaryPanel
        operationDetail={{ status: 'available', data: null }}
        serverStatus={{
          status: 'available',
          data: { isRunning: true, isReady: false },
        }}
        startupSchema={startupSchema}
        startupSnapshot={{
          status: 'available',
          data: {
            sessionId: 'stp_run-a',
            started: false,
            milestones: ['reached', 'skipped', 'pending'],
          },
        }}
        {...overrides}
      />
    </MemoryRouter>,
  )
}

describe('ServerSummaryPanel', () => {
  it('shows a compact authoritative startup count and no detailed milestones', () => {
    renderSummary()

    const summary = screen.getByRole('article', { name: 'Server summary' })
    expect(summary).toHaveTextContent('Startup: 2 / 3 milestones')
    expect(summary).not.toHaveTextContent('Unity Engine')
    expect(summary).not.toHaveTextContent('Mono Runtime')
    expect(summary).not.toHaveTextContent('World loading')
    expect(summary).not.toHaveTextContent('%')
    expect(screen.getByRole('link', { name: 'Manage server' })).toHaveAttribute(
      'href',
      '/dashboard/server',
    )
  })

  it('does not retain startup detail when the server is ready', () => {
    renderSummary({
      serverStatus: {
        status: 'available',
        data: { isRunning: true, isReady: true },
      },
    })

    expect(screen.queryByText(/Startup:/)).not.toBeInTheDocument()
    expect(screen.queryByText('Unity Engine')).not.toBeInTheDocument()
  })

  it('reports startup information as unavailable without inventing a failure state', () => {
    renderSummary({
      startupSnapshot: {
        status: 'unavailable',
        message: 'Startup service did not respond.',
      },
    })

    expect(screen.getByText('Startup information unavailable.')).toBeVisible()
    expect(screen.queryByText(/failed/i)).not.toBeInTheDocument()
  })

  it('presents an active wipe as Maintenance over the stopped server state', () => {
    renderSummary({
      serverStatus: {
        status: 'available',
        data: { isRunning: false, isReady: false },
      },
      operationDetail: {
        status: 'available',
        data: {
          id: 'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
          type: 'Wipe',
          status: 'Running',
          createdAt: '2026-09-30T08:30:00Z',
        },
      },
    })

    const summary = screen.getByRole('article', { name: 'Server summary' })
    expect(summary).toHaveTextContent('Maintenance')
    expect(summary).toHaveTextContent('Wipe is running.')
  })
})
