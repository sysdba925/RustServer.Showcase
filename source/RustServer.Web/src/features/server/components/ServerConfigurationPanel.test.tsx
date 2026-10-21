import { fireEvent, render, screen } from '@testing-library/react'
import type { ComponentProps } from 'react'
import { describe, expect, it, vi } from 'vitest'

import type { ServerConfigurationResponse } from '../../../shared/api'
import { ServerConfigurationPanel } from './ServerConfigurationPanel'

const response: ServerConfigurationResponse = {
  desired: {
    hostname: 'Desired server',
    description: 'Desired description',
    maxPlayers: 50,
    tickRate: 30,
    saveInterval: 300,
    seed: 12345,
    worldSize: 4200,
  },
  effective: {
    hostname: 'Current server',
    description: 'Current description',
    maxPlayers: 25,
    tickRate: 20,
    saveInterval: 600,
    seed: 67890,
    worldSize: 3500,
  },
}

function renderPanel(
  overrides: Partial<ComponentProps<typeof ServerConfigurationPanel>> = {},
) {
  const onSave = vi.fn()

  render(
    <ServerConfigurationPanel
      configuration={{ status: 'available', data: response }}
      mutationsBlocked={false}
      onSave={onSave}
      pendingSave={false}
      saveError={null}
      saveSucceeded={false}
      {...overrides}
    />,
  )

  return onSave
}

describe('ServerConfigurationPanel', () => {
  it('loads desired values and shows only differing effective values', async () => {
    renderPanel()

    expect(await screen.findByDisplayValue('Desired server')).toBeVisible()
    expect(screen.getByDisplayValue('12345')).toBeVisible()
    expect(screen.getAllByText(/Current:/)).not.toHaveLength(0)
  })

  it('does not add effective-value noise when desired and effective match', async () => {
    renderPanel({
      configuration: {
        status: 'available',
        data: { desired: response.desired, effective: response.desired },
      },
    })

    await screen.findByDisplayValue('Desired server')
    expect(screen.queryByText(/Current:/)).not.toBeInTheDocument()
  })

  it('explains when Control has no effective configuration', async () => {
    renderPanel({
      configuration: {
        status: 'available',
        data: { desired: response.desired, effective: null },
      },
    })

    expect(
      await screen.findByText('Current configuration has not been confirmed by Control yet.'),
    ).toBeVisible()
  })

  it('keeps edits local until the operator explicitly saves a complete payload', async () => {
    const onSave = renderPanel()

    fireEvent.change(await screen.findByLabelText(/^Seed/), {
      target: { value: '99999' },
    })

    expect(onSave).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Save configuration' }))
    expect(onSave).toHaveBeenCalledWith({
      ...response.desired,
      seed: 99999,
    })
  })

  it('shows save errors without replacing the draft', async () => {
    renderPanel({ saveError: 'Seed must be within the configured range.' })

    expect(
      await screen.findByText('Seed must be within the configured range.'),
    ).toBeVisible()
    expect(screen.getByDisplayValue('12345')).toBeVisible()
  })
})
