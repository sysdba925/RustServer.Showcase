import { fireEvent, render, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { ControlPlaneContext } from '../control-plane/ControlPlaneContext'
import { createControlPlaneContextValue } from '../control-plane/fixtures/create-control-plane-context-value'
import { WorldPage } from './WorldPage'

// Snapshot API and polling remain covered by SnapshotsPanel unit tests.
vi.mock('./components/SnapshotsPanel', () => ({
  SnapshotsPanel: ({ compact }: { compact?: boolean }) => (
    <article aria-label="Snapshots" data-compact={String(compact)} />
  ),
}))

describe('World V1 layout', () => {
  it('keeps next-world saving with full configuration and a separate Snapshots column', () => {
    const saveConfiguration = vi.fn()
    render(
      <ControlPlaneContext.Provider
        value={createControlPlaneContextValue({ saveConfiguration })}
      >
        <WorldPage />
      </ControlPlaneContext.Provider>,
    )

    expect(screen.getByRole('heading', { name: 'World', level: 1 })).toBeVisible()
    expect(screen.getByRole('region', { name: 'Next World' })).toBeVisible()
    expect(screen.getByRole('region', { name: 'Wipe' })).toBeVisible()
    expect(screen.getByRole('article', { name: 'Snapshots' })).toHaveAttribute('data-compact', 'true')
    expect(screen.getByText('NEXT START')).toBeVisible()

    const nextWorld = screen.getByRole('region', { name: 'Next World' })
    expect(within(nextWorld).getByText('Current: 67890')).toBeVisible()
    fireEvent.change(within(nextWorld).getByLabelText(/^Seed/), {
      target: { value: '34567' },
    })
    fireEvent.click(within(nextWorld).getByRole('button', { name: 'Save configuration' }))
    expect(saveConfiguration).toHaveBeenCalledWith(expect.objectContaining({
      seed: 34567, worldSize: 4200, hostname: 'RustServer Staging',
    }))
  })

  it('preserves the destructive wipe guard', () => {
    const requestWipe = vi.fn()
    render(
      <ControlPlaneContext.Provider value={createControlPlaneContextValue({
        wipeUnavailableReason: 'Server must be stopped to wipe.',
        requestWipe,
      })}>
        <WorldPage />
      </ControlPlaneContext.Provider>,
    )
    const wipe = screen.getByRole('region', { name: 'Wipe' })
    expect(within(wipe).getByText('Stopped server required')).toBeVisible()
    expect(within(wipe).getByRole('button', { name: 'Execute wipe' })).toBeDisabled()
    expect(requestWipe).not.toHaveBeenCalled()
  })
})
