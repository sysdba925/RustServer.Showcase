import { MemoryRouter, Route, Routes } from 'react-router-dom'

import type { Meta, StoryObj } from '@storybook/react-vite'

import {
  incomingChanges,
  inspectedUpdatePackage,
} from './fixtures/plugin-fixtures'
import { PluginsUpdatePackagePage } from './PluginsUpdatePackagePage'
import { PluginsApiMock } from './storybook/PluginsApiMock'
import { createPluginsApiResponder } from './storybook/plugins-api-responder'

type Story = StoryObj<typeof PluginsUpdatePackagePage>

const packagePath = `/api/update-packages/${inspectedUpdatePackage.id}`
const responder = createPluginsApiResponder({
  [`GET ${packagePath}`]: inspectedUpdatePackage,
  [`GET ${packagePath}/changes`]: {
    packageId: inspectedUpdatePackage.id,
    workspaceHeads: [
      { repository: 'plugins', head: 'f19bd92e1a4c' },
      { repository: 'oxideConfigs', head: 'e0d90477ec44' },
    ],
    changes: incomingChanges,
  },
})

const meta = {
  title: 'Plugins/Pages/Update Package',
  component: PluginsUpdatePackagePage,
  decorators: [
    (Story) => (
      <MemoryRouter
        initialEntries={[`/plugins/updates/${inspectedUpdatePackage.id}`]}
      >
        <PluginsApiMock responder={responder}>
          <Routes>
            <Route path="/plugins/updates/:packageId" element={<Story />} />
          </Routes>
        </PluginsApiMock>
      </MemoryRouter>
    ),
  ],
} satisfies Meta<typeof PluginsUpdatePackagePage>

export default meta

export const Ready: Story = {}
