import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import type { ServerConfigurationResponse } from '../../../shared/api'
import { NextWorldConfigurationPanel } from './NextWorldConfigurationPanel'

const configuration: ServerConfigurationResponse = {
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
    tickRate: 30,
    saveInterval: 300,
    seed: 67890,
    worldSize: 3500,
  },
}

describe('NextWorldConfigurationPanel', () => {
  it('saves a complete desired configuration when only next-world fields change', async () => {
    const onSave = vi.fn()

    render(
      <NextWorldConfigurationPanel
        configuration={{ status: 'available', data: configuration }}
        mutationsBlocked={false}
        onSave={onSave}
        pendingSave={false}
        saveError={null}
        saveSucceeded={false}
      />,
    )

    fireEvent.change(await screen.findByLabelText(/^Seed/), {
      target: { value: '24680' },
    })
    fireEvent.change(screen.getByLabelText(/^World size/), {
      target: { value: '4500' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Save configuration' }))

    expect(onSave).toHaveBeenCalledWith({
      ...configuration.desired,
      seed: 24680,
      worldSize: 4500,
    })
  })

  it('shows only changed current values', async () => {
    render(
      <NextWorldConfigurationPanel
        configuration={{ status: 'available', data: configuration }}
        mutationsBlocked={false}
        onSave={vi.fn()}
        pendingSave={false}
        saveError={null}
        saveSucceeded={false}
      />,
    )

    expect(await screen.findByText('Current: 67890')).toBeVisible()
    expect(screen.getByText('Current: 3500')).toBeVisible()
  })

  it('retains the full desired save payload and current values in compact World layout', () => {
    const onSave = vi.fn()
    render(
      <NextWorldConfigurationPanel
        compact
        configuration={{ status: 'available', data: configuration }}
        mutationsBlocked={false}
        onSave={onSave}
        pendingSave={false}
        saveError={null}
        saveSucceeded={false}
      />,
    )

    expect(screen.getByText('Current: 67890')).toBeVisible()
    expect(screen.getByText('Current: 3500')).toBeVisible()
    fireEvent.change(screen.getByLabelText(/^Seed/), { target: { value: '24680' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save configuration' }))
    expect(onSave).toHaveBeenCalledWith({ ...configuration.desired, seed: 24680 })
  })

})
