import type { Meta, StoryObj } from '@storybook/react-vite'
import { CatalogPrototype } from './CatalogPrototype'

const meta = { title: 'Plugin Catalog Prototype/Catalog', component: CatalogPrototype, parameters: { layout: 'fullscreen' } } satisfies Meta<typeof CatalogPrototype>
export default meta
type Story = StoryObj<typeof meta>
export const Library: Story = {}
