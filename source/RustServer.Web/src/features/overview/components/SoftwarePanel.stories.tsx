import type { ComponentProps } from 'react'

import type { Meta, StoryObj } from '@storybook/react-vite'
import { fn } from 'storybook/test'

import { controlPlaneScenarios } from '../../control-plane/fixtures/control-plane-scenarios'
import { deriveActionAvailability } from '../../control-plane/model'
import { SoftwarePanel } from './SoftwarePanel'

type Story = StoryObj<typeof SoftwarePanel>
type Props = ComponentProps<typeof SoftwarePanel>

const readyArgs = {
  actionAvailability: deriveActionAvailability(controlPlaneScenarios.ready),
  actionsBlocked: false,
  pendingUpdateTarget: null,
  rustUpdateError: null,
  oxideUpdateError: null,
  rustUpdateUnavailableReason: null,
  oxideUpdateUnavailableReason: null,
  onRustUpdate: fn(),
  onOxideUpdate: fn(),
  rustVersions: controlPlaneScenarios.ready.rustVersions,
  oxideVersions: controlPlaneScenarios.ready.oxideVersions,
} satisfies Props

const meta = {
  title: 'Overview/Software',
  component: SoftwarePanel,
  args: readyArgs,
} satisfies Meta<typeof SoftwarePanel>

export default meta

export const Ready: Story = {}

export const RustUpdateAvailable: Story = {
  args: {
    ...readyArgs,
    actionAvailability: deriveActionAvailability(
      controlPlaneScenarios['rust-update-available'],
    ),
    rustVersions: controlPlaneScenarios['rust-update-available'].rustVersions,
    oxideVersions: controlPlaneScenarios['rust-update-available'].oxideVersions,
  },
}

export const OxideUpdateAvailable: Story = {
  args: {
    ...readyArgs,
    actionAvailability: deriveActionAvailability(
      controlPlaneScenarios['oxide-update-available'],
    ),
    rustVersions: controlPlaneScenarios['oxide-update-available'].rustVersions,
    oxideVersions: controlPlaneScenarios['oxide-update-available'].oxideVersions,
  },
}

export const UpdatePending: Story = {
  args: {
    ...readyArgs,
    actionAvailability: deriveActionAvailability(
      controlPlaneScenarios['rust-update-available'],
    ),
    actionsBlocked: true,
    pendingUpdateTarget: 'rust',
    rustVersions: controlPlaneScenarios['rust-update-available'].rustVersions,
    oxideVersions: controlPlaneScenarios['rust-update-available'].oxideVersions,
  },
}

export const UpdateError: Story = {
  args: {
    ...readyArgs,
    actionAvailability: deriveActionAvailability(
      controlPlaneScenarios['oxide-update-available'],
    ),
    rustVersions: controlPlaneScenarios['oxide-update-available'].rustVersions,
    oxideVersions: controlPlaneScenarios['oxide-update-available'].oxideVersions,
    oxideUpdateError: 'The Oxide update request was rejected.',
  },
}

export const PartiallyUnavailable: Story = {
  args: {
    ...readyArgs,
    actionAvailability: deriveActionAvailability(
      controlPlaneScenarios['partial-unavailable'],
    ),
    rustVersions: controlPlaneScenarios['partial-unavailable'].rustVersions,
    oxideVersions: controlPlaneScenarios['partial-unavailable'].oxideVersions,
  },
}

export const Loading: Story = {
  args: {
    ...readyArgs,
    rustVersions: { status: 'loading' },
    oxideVersions: { status: 'loading' },
  },
}
