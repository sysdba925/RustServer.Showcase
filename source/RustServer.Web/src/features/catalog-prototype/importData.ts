import { createTag, makePlugin, uniqueSlug } from './library'
import type { LibraryPlugin, PrototypeLibrary } from './model'

type InputPlugin = { name: string; author?: string; description?: string; notes?: string; sources?: { url: string; site?: string }[]; price?: { amount: number; currency: string }; tags?: string[]; folders?: string[] }
export type ImportRow = { index: number; value?: InputPlugin; errors: string[]; duplicates: string[] }
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value)
export function isHttpUrl(value: string) { try { return ['http:', 'https:'].includes(new URL(value).protocol) } catch { return false } }
const normalize = (s: string) => s.trim().toLowerCase()

export function parseImport(text: string, library: PrototypeLibrary): { error?: string; rows: ImportRow[] } {
  let json: unknown
  try { json = JSON.parse(text) } catch { return { error: 'Invalid JSON. Check the file syntax.', rows: [] } }
  if (!record(json) || json.schemaVersion !== 1 || !Array.isArray(json.plugins) || !json.plugins.length) return { error: 'Expected schemaVersion: 1 and a non-empty plugins array.', rows: [] }
  const seen: { name: string; slug: string; urls: string[]; label: string }[] = library.plugins.map(plugin => ({ name: normalize(plugin.name), slug: uniqueSlug(plugin.name, []), urls: plugin.sources.map(source => normalize(source.url)), label: `library: ${plugin.name}` }))
  return { rows: json.plugins.map((raw: unknown, index: number) => {
    const errors: string[] = []
    if (!record(raw)) return { index, errors: ['Expected an object.'], duplicates: [] }
    for (const key of Object.keys(raw)) if (!['name', 'author', 'description', 'notes', 'sources', 'price', 'tags', 'folders'].includes(key)) errors.push(`Unknown field: ${key}.`)
    if (typeof raw.name !== 'string' || !raw.name.trim()) errors.push('name is required and must be a non-empty string.')
    for (const key of ['author', 'description', 'notes']) if (raw[key] !== undefined && typeof raw[key] !== 'string') errors.push(`${key} must be a string.`)
    for (const key of ['tags', 'folders']) if (raw[key] !== undefined && (!Array.isArray(raw[key]) || !(raw[key] as unknown[]).every(item => typeof item === 'string' && item.trim()))) errors.push(`${key} must be an array of non-empty strings.`)
    if (raw.sources !== undefined && (!Array.isArray(raw.sources) || !raw.sources.every(source => record(source) && typeof source.url === 'string' && isHttpUrl(source.url) && (source.site === undefined || (typeof source.site === 'string' && !!source.site.trim()))))) errors.push('sources must contain HTTP(S) URLs and optional non-empty site names.')
    if (raw.price !== undefined && (!record(raw.price) || typeof raw.price.amount !== 'number' || !Number.isFinite(raw.price.amount) || raw.price.amount < 0 || typeof raw.price.currency !== 'string' || !/^[A-Za-z]{3}$/.test(raw.price.currency))) errors.push('price requires a non-negative amount and a three-letter currency.')
    if (errors.length) return { index, errors, duplicates: [] }
    const value = raw as InputPlugin
    const name = normalize(value.name)
    const slug = uniqueSlug(value.name, [])
    const urls = (value.sources ?? []).map(source => normalize(source.url))
    const duplicates = seen.filter(item => item.name === name || item.slug === slug || urls.some(url => item.urls.includes(url))).map(item => item.label)
    seen.push({ name, slug, urls, label: `row ${index + 1}: ${value.name}` })
    return { index, value, errors, duplicates }
  }) }
}

export function unknownItems(rows: ImportRow[], library: PrototypeLibrary) {
  const values = rows.filter(row => row.value && !row.duplicates.length).map(row => row.value!)
  const missing = (items: string[], known: string[]) => [...new Set(items.map(item => item.trim()).filter(item => item && !known.some(value => normalize(value) === normalize(item))))]
  return {
    sites: missing(values.flatMap(value => value.sources?.map(source => source.site ?? '') ?? []), library.sites),
    tags: missing(values.flatMap(value => value.tags ?? []), library.tags.map(tag => tag.name)),
    folders: missing(values.flatMap(value => value.folders ?? []), library.folders),
  }
}

export function applyImport(rows: ImportRow[], library: PrototypeLibrary): PrototypeLibrary {
  const next = structuredClone(library)
  for (const row of rows) {
    if (!row.value || row.duplicates.length) continue
    const value = row.value
    const plugin: LibraryPlugin = { ...makePlugin(value.name.trim(), next.plugins), author: value.author, sourceDescription: value.description, notes: value.notes, price: value.price ? { ...value.price, currency: value.price.currency.toUpperCase() } : undefined, sources: [], tags: [], folders: [] }
    for (const source of value.sources ?? []) {
      const input = source.site?.trim() ?? ''
      const site = next.sites.find(item => normalize(item) === normalize(input)) ?? input
      if (site && !next.sites.includes(site)) next.sites.push(site)
      plugin.sources.push({ site, url: source.url.trim() })
    }
    for (const input of value.tags ?? []) {
      let tag = next.tags.find(item => normalize(item.name) === normalize(input))
      if (!tag) { tag = createTag(input.trim(), next.tags); next.tags.push(tag) }
      if (!plugin.tags.some(item => item.name === tag.name)) plugin.tags.push(tag)
    }
    for (const input of value.folders ?? []) {
      const folder = next.folders.find(item => normalize(item) === normalize(input)) ?? input.trim()
      if (!next.folders.includes(folder)) next.folders.push(folder)
      if (!plugin.folders.includes(folder)) plugin.folders.push(folder)
    }
    next.plugins.push(plugin)
  }
  return next
}
