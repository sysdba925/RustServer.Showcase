import type { Meta, StoryObj } from '@storybook/react-vite'
import { fn } from 'storybook/test'

import { runtimeChanges } from '../fixtures/plugin-fixtures'
import { RuntimeDiscardReview } from './RuntimeChangeReview'

type Story = StoryObj<typeof RuntimeDiscardReview>

const meta = {
  title: 'Plugins/Runtime Changes/Discard Review',
  component: RuntimeDiscardReview,
  args: {
    selectedChanges: runtimeChanges,
    discarding: false,
    onConfirm: fn(),
    onCancel: fn(),
  },
} satisfies Meta<typeof RuntimeDiscardReview>

export default meta

export const Ready: Story = {}

export const InProgress: Story = {
  args: {
    discarding: true,
  },
}

export const EmptySelection: Story = {
  args: {
    selectedChanges: [],
  },
}
