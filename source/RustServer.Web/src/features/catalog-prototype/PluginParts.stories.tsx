import type { Meta, StoryObj } from '@storybook/react-vite'
import { getPrototypePlugins } from './data'
import { PluginCard, PluginRow, Price, SourceList, TagList } from './PluginParts'

const plugins = getPrototypePlugins()
const meta = { title: 'Plugin Catalog Prototype/Components', component: PluginCard } satisfies Meta<typeof PluginCard>
export default meta
type Story = StoryObj<typeof meta>
export const Card: Story = { args: { plugin: plugins[2] } }
export const TableRow: Story = { args: { plugin: plugins[2] }, render: args => <table><tbody><PluginRow {...args} /></tbody></table> }
export const LibraryTags: Story = { args: { plugin: plugins[2] }, render: args => <TagList tags={args.plugin.tags} /> }
export const Sources: Story = { args: { plugin: plugins[6] }, render: args => <SourceList sources={args.plugin.sources} /> }
export const Prices: Story = { args: { plugin: plugins[2] }, render: args => <div style={{ display: 'flex', gap: '1rem' }}><Price price={args.plugin.price} /><Price price={{ amount: 0, currency: 'EUR' }} /><Price /></div> }
export const PriceStatuses: Story = { args: { plugin: plugins[2] }, render: () => <div style={{ display: 'flex', gap: '1rem' }}>{['Purchased', 'Planned purchase', 'Pirated / Unlicensed', 'Other'].map(name => <div key={name}>{name}: <Price price={{ amount: 14.99, currency: 'USD' }} tags={[{ name, color: '#aaa' }]} /></div>)}</div> }
