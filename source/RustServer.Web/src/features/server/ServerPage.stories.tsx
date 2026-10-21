import { MemoryRouter } from 'react-router-dom'

import type { Meta, StoryObj } from '@storybook/react-vite'
import { fn } from 'storybook/test'

import { ControlPlaneContext, type ControlPlaneContextValue } from '../control-plane/ControlPlaneContext'
import { createControlPlaneContextValue } from '../control-plane/fixtures/create-control-plane-context-value'
import {
  controlPlaneScenarios,
  startupInProgress,
  startupCompleted,
} from '../control-plane/fixtures/control-plane-scenarios'
import { deriveActionAvailability, type ControlPlaneInput } from '../control-plane/model'
import { ServerPage } from './ServerPage'

type Story = StoryObj<typeof ServerPage>

function renderPage(value: ControlPlaneContextValue) {
  return (
    <MemoryRouter>
      <ControlPlaneContext.Provider value={value}>
        <ServerPage />
      </ControlPlaneContext.Provider>
    </MemoryRouter>
  )
}

const meta = {
  title: 'Server/Page',
  component: ServerPage,
} satisfies Meta<typeof ServerPage>

export default meta

export const Ready: Story = {
  render: () =>
    renderPage(
      createControlPlaneContextValue({
        start: fn(),
        stop: fn(),
        restart: fn(),
        saveConfiguration: fn(),
        startupSnapshot: { status: 'available', data: startupCompleted },
      }),
    ),
}

export const Starting: Story = {
  render: () => {
    const starting = controlPlaneScenarios.starting

    return renderPage(
      createControlPlaneContextValue({
        serverStatus: starting.serverStatus,
        rustVersions: starting.rustVersions,
        oxideVersions: starting.oxideVersions,
        actionAvailability: deriveActionAvailability(starting),
        startupSnapshot: { status: 'available', data: startupInProgress },
        start: fn(),
        stop: fn(),
        restart: fn(),
        saveConfiguration: fn(),
      }),
    )
  },
}

export const FreshInstall: Story = {
  render: () => {
    const stopped = controlPlaneScenarios.stopped
    const fresh: ControlPlaneInput = {
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

    return renderPage(
      createControlPlaneContextValue({
        serverStatus: fresh.serverStatus,
        rustVersions: fresh.rustVersions,
        oxideVersions: fresh.oxideVersions,
        actionAvailability: deriveActionAvailability(fresh),
        requestRustUpdate: fn(),
        requestOxideUpdate: fn(),
      }),
    )
  },
}
