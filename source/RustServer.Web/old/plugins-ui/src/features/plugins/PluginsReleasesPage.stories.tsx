import { MemoryRouter } from 'react-router-dom'

import type { Meta, StoryObj } from '@storybook/react-vite'

import { releases } from './fixtures/plugin-fixtures'
import { PluginsReleasesPage } from './PluginsReleasesPage'
import { PluginsApiMock } from './storybook/PluginsApiMock'
import { createPluginsApiResponder } from './storybook/plugins-api-responder'

type Story = StoryObj<typeof PluginsReleasesPage>

const responder = createPluginsApiResponder({
  'GET /api/releases': releases,
})

const meta = {
  title: 'Plugins/Pages/Releases',
  component: PluginsReleasesPage,
  decorators: [
    (Story) => (
      <MemoryRouter initialEntries={['/plugins/releases']}>
        <PluginsApiMock responder={responder}>
          <Story />
        </PluginsApiMock>
      </MemoryRouter>
    ),
  ],
} satisfies Meta<typeof PluginsReleasesPage>

export default meta

export const Ready: Story = {}
