import { MemoryRouter } from 'react-router-dom'

import type { Meta, StoryObj } from '@storybook/react-vite'
import { fn } from 'storybook/test'

import { ControlPlaneContext, type ControlPlaneContextValue } from '../control-plane/ControlPlaneContext'
import { createControlPlaneContextValue } from '../control-plane/fixtures/create-control-plane-context-value'
import { controlPlaneScenarios } from '../control-plane/fixtures/control-plane-scenarios'
import { WorldPage } from './WorldPage'

type Story = StoryObj<typeof WorldPage>

function renderPage(value: ControlPlaneContextValue) {
  return (
    <MemoryRouter>
      <ControlPlaneContext.Provider value={value}>
        <WorldPage />
      </ControlPlaneContext.Provider>
    </MemoryRouter>
  )
}

const meta = {
  title: 'World/Page',
  component: WorldPage,
} satisfies Meta<typeof WorldPage>

export default meta

export const Ready: Story = {
  render: () =>
    renderPage(
      createControlPlaneContextValue({
        saveConfiguration: fn(),
        requestWipe: fn(),
      }),
    ),
}

export const WipeBlocked: Story = {
  render: () =>
    renderPage(
      createControlPlaneContextValue({
        wipeUnavailableReason: 'Server must be stopped to wipe.',
        saveConfiguration: fn(),
        requestWipe: fn(),
      }),
    ),
}

export const Stopped: Story = {
  render: () =>
    renderPage(
      createControlPlaneContextValue({
        serverStatus: controlPlaneScenarios.stopped.serverStatus,
        wipeUnavailableReason: null,
        saveConfiguration: fn(),
        requestWipe: fn(),
      }),
    ),
}
