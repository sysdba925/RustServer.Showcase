import type { Meta, StoryObj } from '@storybook/react-vite'

import {
  committedUpdatePackage,
  failedUpdatePackage,
  inspectedUpdatePackage,
} from '../fixtures/plugin-fixtures'
import { UpdatePackageSummary } from './UpdatePackageSummary'

type Story = StoryObj<typeof UpdatePackageSummary>

const meta = {
  title: 'Plugins/Update Package/Summary',
  component: UpdatePackageSummary,
  args: {
    updatePackage: inspectedUpdatePackage,
  },
} satisfies Meta<typeof UpdatePackageSummary>

export default meta

export const Inspected: Story = {}

export const Committed: Story = {
  args: {
    updatePackage: committedUpdatePackage,
  },
}

export const InspectionFailed: Story = {
  args: {
    updatePackage: failedUpdatePackage,
  },
}
