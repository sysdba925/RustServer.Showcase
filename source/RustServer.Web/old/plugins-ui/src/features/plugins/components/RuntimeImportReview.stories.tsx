import type { Meta, StoryObj } from '@storybook/react-vite'
import { fn } from 'storybook/test'

import { runtimeChanges } from '../fixtures/plugin-fixtures'
import { RuntimeImportReview } from './RuntimeChangeReview'

type Story = StoryObj<typeof RuntimeImportReview>

const meta = {
  title: 'Plugins/Runtime Changes/Import Review',
  component: RuntimeImportReview,
  args: {
    selectedChanges: runtimeChanges.slice(0, 2),
    message: 'Import Staging runtime changes into Development',
    importing: false,
    onMessageChange: fn(),
    onConfirm: fn(),
    onCancel: fn(),
  },
} satisfies Meta<typeof RuntimeImportReview>

export default meta

export const Ready: Story = {}

export const InProgress: Story = {
  args: {
    importing: true,
  },
}

export const EmptySelection: Story = {
  args: {
    selectedChanges: [],
    message: '',
  },
}
