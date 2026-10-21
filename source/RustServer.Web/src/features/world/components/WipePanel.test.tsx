import { fireEvent, render, screen } from '@testing-library/react'
import type { ComponentProps } from 'react'
import { describe, expect, it, vi } from 'vitest'

import type { WipePlanRequest } from '../../../shared/api'
import { WipePanel } from './WipePanel'

function renderWipePanel(
  overrides: Partial<ComponentProps<typeof WipePanel>> = {},
) {
  const onWipe = vi.fn()

  render(
    <WipePanel
      mutationsBlocked={false}
      onWipe={onWipe}
      operationDetail={{ status: 'available', data: null }}
      pendingWipe={false}
      unavailableReason={null}
      wipeError={null}
      {...overrides}
    />,
  )

  return onWipe
}

describe('WipePanel', () => {
  it.each([
    ['delete', 'delete'],
    ['delete', 'keep'],
    ['keep', 'delete'],
    ['keep', 'keep'],
  ] as const)('confirms and sends World %s / Blueprints %s', (world, blueprints) => {
    const onWipe = renderWipePanel()

    const worldGroup = screen.getByRole('group', { name: 'World' })
    fireEvent.click(
      worldGroup.querySelector<HTMLInputElement>(`input[value="${world}"]`)!,
    )
    const blueprintsGroup = screen.getByRole('group', { name: 'Blueprints' })
    fireEvent.click(
      blueprintsGroup.querySelector<HTMLInputElement>(
        `input[value="${blueprints}"]`,
      )!,
    )
    fireEvent.click(screen.getByRole('button', { name: 'Execute wipe' }))

    const confirmation = screen.getByRole('dialog', { name: 'Confirm wipe plan' })
    expect(confirmation).toHaveTextContent(`World${world === 'delete' ? 'Delete' : 'Keep'}`)
    expect(confirmation).toHaveTextContent(
      `Blueprints${blueprints === 'delete' ? 'Delete' : 'Keep'}`,
    )
    fireEvent.click(
      screen.getByRole('button', { name: 'Execute wipe', hidden: true }),
    )

    const expected: WipePlanRequest = { world, blueprints, pluginData: [] }
    expect(onWipe).toHaveBeenCalledWith(expected)
  })

  it('cancels confirmation without requesting a wipe', () => {
    const onWipe = renderWipePanel()

    fireEvent.click(screen.getByRole('button', { name: 'Execute wipe' }))
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))

    expect(onWipe).not.toHaveBeenCalled()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('explains why wipe is disabled while the server is running', () => {
    renderWipePanel({ unavailableReason: 'Server must be stopped to wipe.' })

    expect(screen.getByRole('button', { name: 'Execute wipe' })).toBeDisabled()
    expect(screen.getByText('Server must be stopped to wipe.')).toBeVisible()
  })

  it('does not allow a concurrent destructive action while another mutation blocks it', () => {
    renderWipePanel({
      mutationsBlocked: true,
      unavailableReason: 'Another server mutation is in progress.',
    })

    expect(screen.getByRole('button', { name: 'Execute wipe' })).toBeDisabled()
  })

  it('does not mislabel an active update as a wipe', () => {
    renderWipePanel({
      unavailableReason: 'Another server mutation is in progress.',
      operationDetail: {
        status: 'available',
        data: {
          id: 'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
          type: 'RustUpdate',
          status: 'Running',
          createdAt: '2026-09-30T08:30:00Z',
        },
      },
    })

    expect(screen.getByRole('button', { name: 'Execute wipe' })).toBeDisabled()
    expect(screen.queryByText('Wipe is Running.')).not.toBeInTheDocument()
  })

  it('renders a terminal, path-free wipe result', () => {
    renderWipePanel({
      operationDetail: {
        status: 'available',
        data: {
          id: 'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
          type: 'Wipe',
          status: 'Failed',
          createdAt: '2026-09-30T08:30:00Z',
          wipeResult: {
            succeeded: false,
            targets: [
              {
                target: 'world',
                action: 'delete',
                outcome: 'failed',
                affectedItems: 0,
                error: 'The target could not be processed.',
              },
            ],
          },
        },
      },
    })

    const result = screen.getByRole('region', { name: 'Wipe result' })
    expect(result).toHaveTextContent('Failed')
    expect(result).toHaveTextContent('Action: delete; outcome: failed; affected items: 0')
    expect(result).toHaveTextContent('The target could not be processed.')
  })

  it('renders latest wipe result and retains confirmation in World V1 mode', () => {
    const onWipe = renderWipePanel({ compact: true })

    expect(screen.getByText('Latest wipe result')).toBeVisible()
    expect(screen.getByText('No known operation.')).toBeVisible()
    fireEvent.click(screen.getByRole('button', { name: 'Execute wipe' }))
    expect(screen.getByRole('dialog', { name: 'Confirm wipe plan' })).toBeVisible()
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(onWipe).not.toHaveBeenCalled()
  })

  it('retains the detailed wipe outcome in World V1 mode', () => {
    renderWipePanel({
      compact: true,
      operationDetail: {
        status: 'available',
        data: {
          id: 'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
          type: 'Wipe',
          status: 'Succeeded',
          createdAt: '2026-09-30T08:30:00Z',
          wipeResult: {
            succeeded: true,
            targets: [{ target: 'world', action: 'delete', outcome: 'deleted', affectedItems: 1 }],
          },
        },
      },
    })

    const result = screen.getByRole('region', { name: 'Wipe result' })
    expect(result).toHaveTextContent('Succeeded')
    expect(result).toHaveTextContent('affected items: 1')
    expect(screen.getByText('Latest wipe result')).toBeVisible()
  })

})
