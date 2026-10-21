import type { Meta, StoryObj } from '@storybook/react-vite'
import { fn } from 'storybook/test'

import { incomingChanges, inspectedUpdatePackage } from '../fixtures/plugin-fixtures'
import { CommitUpdateReview } from './CommitUpdateReview'

type Story = StoryObj<typeof CommitUpdateReview>

const meta = {
  title: 'Plugins/Update Package/Commit Review',
  component: CommitUpdateReview,
  args: {
    filename: inspectedUpdatePackage.originalFileName,
    selectedChanges: incomingChanges.slice(0, 2),
    message: 'Update Raidable Bases',
    committing: false,
    onMessageChange: fn(),
    onCommit: fn(),
  },
} satisfies Meta<typeof CommitUpdateReview>

export default meta

export const Ready: Story = {}

export const Committing: Story = {
  args: {
    committing: true,
  },
}

export const EmptySelection: Story = {
  args: {
    selectedChanges: [],
    message: '',
  },
}
