import { MemoryRouter } from 'react-router-dom'

import type { Meta, StoryObj } from '@storybook/react-vite'

import { pluginsEnvironment, productionDeployment, releases } from './fixtures/plugin-fixtures'
import { PluginsDeployPage } from './PluginsDeployPage'
import { PluginsApiMock } from './storybook/PluginsApiMock'
import { createPluginsApiResponder } from './storybook/plugins-api-responder'

type Story = StoryObj<typeof PluginsDeployPage>

const responder = createPluginsApiResponder({
  'GET /api/environment': pluginsEnvironment,
  'GET /api/releases': releases,
  'GET /api/production/deployment': productionDeployment,
})

const meta = {
  title: 'Plugins/Pages/Deploy',
  component: PluginsDeployPage,
  decorators: [
    (Story) => (
      <MemoryRouter initialEntries={['/plugins/deploy']}>
        <PluginsApiMock responder={responder}>
          <Story />
        </PluginsApiMock>
      </MemoryRouter>
    ),
  ],
} satisfies Meta<typeof PluginsDeployPage>

export default meta

export const Ready: Story = {}
