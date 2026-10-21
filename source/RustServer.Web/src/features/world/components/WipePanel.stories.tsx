import type { ComponentProps } from 'react'

import type { Meta, StoryObj } from '@storybook/react-vite'
import { fn, userEvent, within } from 'storybook/test'

import {
  activeWipeOperation,
  completedWipeOperation,
  noKnownOperation,
} from '../../control-plane/fixtures/control-plane-scenarios'
import { WipePanel } from './WipePanel'

type Story = StoryObj<typeof WipePanel>
type Props = ComponentProps<typeof WipePanel>

const availableArgs = {
  mutationsBlocked: false,
  operationDetail: noKnownOperation,
  pendingWipe: false,
  unavailableReason: null,
  wipeError: null,
  onWipe: fn(),
} satisfies Props

const meta = {
  title: 'World/Panels/Wipe',
  component: WipePanel,
  args: availableArgs,
} satisfies Meta<typeof WipePanel>

export default meta

export const Available: Story = {}

export const Confirmation: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    await userEvent.click(canvas.getByRole('button', { name: 'Execute wipe' }))
  },
}

export const InProgress: Story = {
  args: {
    ...availableArgs,
    mutationsBlocked: true,
    pendingWipe: true,
    operationDetail: { status: 'available', data: activeWipeOperation },
    unavailableReason: 'Another server mutation is in progress.',
  },
}

export const Blocked: Story = {
  args: {
    ...availableArgs,
    unavailableReason: 'Server must be stopped to wipe.',
  },
}

export const Error: Story = {
  args: {
    ...availableArgs,
    wipeError: 'The wipe request was rejected.',
  },
}

export const Completed: Story = {
  args: {
    ...availableArgs,
    operationDetail: { status: 'available', data: completedWipeOperation },
  },
}
