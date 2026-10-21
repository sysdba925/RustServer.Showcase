import type { Meta, StoryObj } from '@storybook/react-vite'
import { fn } from 'storybook/test'

import { incomingChanges } from '../fixtures/plugin-fixtures'
import { IncomingChangesPanel } from './IncomingChangesPanel'

type Story = StoryObj<typeof IncomingChangesPanel>

const meta = {
  title: 'Plugins/Incoming Changes',
  component: IncomingChangesPanel,
  args: {
    changes: incomingChanges,
    selectedPaths: new Set<string>(),
    onSelectionChange: fn(),
    onSelectAll: fn(),
    onViewDiff: fn(),
  },
} satisfies Meta<typeof IncomingChangesPanel>

export default meta

export const MixedChanges: Story = {}

export const SelectedManagedChanges: Story = {
  args: {
    selectedPaths: new Set([
      'oxide/plugins/RaidableBases.cs',
      'oxide/config/RaidableBases.json',
    ]),
  },
}

export const Empty: Story = {
  args: {
    changes: [],
  },
}
