import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { HttpError } from '../../../shared/api'
import type { NetworkAccessApi } from './NetworkAccessPanel'
import { NetworkAccessPanel } from './NetworkAccessPanel'

describe('NetworkAccessPanel', () => {
  it('keeps the persisted state in the compact trigger until explicit Save', async () => {
    const api: NetworkAccessApi = {
      getNetworkAccess: vi.fn().mockResolvedValue({ publicEnabled: true, maintenancePort: 50015 }),
      updateNetworkAccess: vi.fn().mockResolvedValue({ publicEnabled: false, maintenancePort: 50016 }),
    }

    render(<NetworkAccessPanel api={api} />)

    const trigger = await screen.findByRole('button', {
      name: 'Network access: Public ON, maintenance port 50015',
    })
    expect(trigger).toHaveAttribute('aria-expanded', 'false')
    expect(trigger).toHaveAttribute('data-state', 'on')
    expect(screen.queryByRole('switch')).not.toBeInTheDocument()

    fireEvent.click(trigger)
    const publicAccess = screen.getByRole('switch', { name: 'Public Access' })
    expect(publicAccess).toBeChecked()
    fireEvent.click(publicAccess)
    fireEvent.change(screen.getByLabelText('Maintenance Port'), { target: { value: '50016' } })

    expect(screen.getByText(/may disconnect the current game connection/i)).toBeVisible()
    expect(trigger).toHaveAttribute('data-state', 'on')
    expect(api.updateNetworkAccess).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => {
      expect(api.updateNetworkAccess).toHaveBeenCalledWith(
        { publicEnabled: false, maintenancePort: 50016 },
        expect.any(AbortSignal),
      )
    })
    expect(await screen.findByRole('button', {
      name: 'Network access: Public OFF, maintenance port 50016',
    })).toHaveAttribute('data-state', 'off')
    expect(screen.queryByRole('switch')).not.toBeInTheDocument()
  })

  it('discards unsaved edits on Cancel', async () => {
    const api: NetworkAccessApi = {
      getNetworkAccess: vi.fn().mockResolvedValue({ publicEnabled: true, maintenancePort: 50015 }),
      updateNetworkAccess: vi.fn(),
    }
    render(<NetworkAccessPanel api={api} />)

    const trigger = await screen.findByRole('button', {
      name: 'Network access: Public ON, maintenance port 50015',
    })
    fireEvent.click(trigger)
    fireEvent.click(screen.getByRole('switch', { name: 'Public Access' }))
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(trigger).toHaveAttribute('aria-expanded', 'false')
    fireEvent.click(trigger)
    expect(screen.getByRole('switch', { name: 'Public Access' })).toBeChecked()
    expect(api.updateNetworkAccess).not.toHaveBeenCalled()
  })

  it('shows a save failure without changing the applied state', async () => {
    const api: NetworkAccessApi = {
      getNetworkAccess: vi.fn().mockResolvedValue({ publicEnabled: true, maintenancePort: 50015 }),
      updateNetworkAccess: vi.fn().mockRejectedValue(new Error('Firewall rules could not be applied.')),
    }

    render(<NetworkAccessPanel api={api} />)
    const trigger = await screen.findByRole('button', {
      name: 'Network access: Public ON, maintenance port 50015',
    })
    fireEvent.click(trigger)
    fireEvent.change(screen.getByLabelText('Maintenance Port'), { target: { value: '50016' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Network access was not saved. Firewall rules could not be applied.',
    )
    expect(trigger).toHaveAttribute('data-state', 'on')
    expect(trigger).toHaveAttribute('aria-expanded', 'true')
  })
  it('shows the backend failure code and safe diagnostic details', async () => {
    const api: NetworkAccessApi = {
      getNetworkAccess: vi.fn().mockResolvedValue({ publicEnabled: true, maintenancePort: 50015 }),
      updateNetworkAccess: vi.fn().mockRejectedValue(new HttpError({
        method: 'PUT',
        url: '/api/server/network-access',
        status: 502,
        statusText: 'Bad Gateway',
        problemDetails: {
          detail: 'NetworkAccess helper is unavailable. Check its systemd service.',
          code: 'HelperUnavailable',
        },
      })),
    }

    render(<NetworkAccessPanel api={api} />)
    fireEvent.click(await screen.findByRole('button', {
      name: 'Network access: Public ON, maintenance port 50015',
    }))
    fireEvent.click(screen.getByRole('switch', { name: 'Public Access' }))
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Code: HelperUnavailable')
    expect(screen.getByRole('alert')).toHaveTextContent('Check its systemd service')
  })

})
