import { MemoryRouter } from 'react-router-dom'

import type { Meta, StoryObj } from '@storybook/react-vite'

import { ControlPlaneContext } from '../control-plane/ControlPlaneContext'
import { createControlPlaneContextValue } from '../control-plane/fixtures/create-control-plane-context-value'
import { pluginsEnvironment, productionDeployment } from './fixtures/plugin-fixtures'
import { PluginsOverviewPage } from './PluginsOverviewPage'
import { PluginsApiMock } from './storybook/PluginsApiMock'
import { createPluginsApiResponder } from './storybook/plugins-api-responder'

type Story = StoryObj<typeof PluginsOverviewPage>

const responder = createPluginsApiResponder({
  'GET /api/environment': pluginsEnvironment,
  'GET /api/production/deployment': productionDeployment,
})

const meta = {
  title: 'Plugins/Pages/Overview',
  component: PluginsOverviewPage,
  decorators: [
    (Story) => (
      <MemoryRouter initialEntries={['/plugins']}>
        <PluginsApiMock responder={responder}>
          <ControlPlaneContext.Provider value={createControlPlaneContextValue()}>
            <Story />
          </ControlPlaneContext.Provider>
        </PluginsApiMock>
      </MemoryRouter>
    ),
  ],
} satisfies Meta<typeof PluginsOverviewPage>

export default meta

export const Ready: Story = {}
