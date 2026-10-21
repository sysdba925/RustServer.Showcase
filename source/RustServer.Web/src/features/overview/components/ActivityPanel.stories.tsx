import type { Meta, StoryObj } from '@storybook/react-vite'

import {
  activeRustUpdateOperation,
  controlPlaneScenarios,
  failedRustUpdateOperation,
} from '../../control-plane/fixtures/control-plane-scenarios'
import { ActivityPanel } from './ActivityPanel'

type Story = StoryObj<typeof ActivityPanel>

const meta = {
  title: 'Overview/Activity',
  component: ActivityPanel,
  args: {
    operationDetail: controlPlaneScenarios.ready.operationDetail,
    acceptedOperation: null,
  },
} satisfies Meta<typeof ActivityPanel>

export default meta

export const Empty: Story = {}

export const InProgress: Story = {
  args: {
    operationDetail: { status: 'available', data: activeRustUpdateOperation },
  },
}

export const Failed: Story = {
  args: {
    operationDetail: { status: 'available', data: failedRustUpdateOperation },
  },
}

export const AcceptedAndLoading: Story = {
  args: {
    operationDetail: { status: 'loading' },
    acceptedOperation: {
      id: activeRustUpdateOperation.id,
      status: 'Pending',
      target: 'rust',
    },
  },
}

export const Unavailable: Story = {
  args: {
    operationDetail: {
      status: 'unavailable',
      message: 'The operation could not be retrieved.',
    },
    acceptedOperation: {
      id: activeRustUpdateOperation.id,
      status: 'Pending',
      target: 'rust',
    },
  },
}
