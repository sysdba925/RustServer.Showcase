// Prototype library data, deliberately independent of backend DTOs.
export interface LibraryTag { name: string; color: string }
export interface LibrarySource { site: string; url: string }
export interface LibraryPlugin {
  id: string
  slug: string
  name: string
  author?: string
  description?: string
  sourceDescription?: string
  lastUpdate?: string
  iconUrl?: string
  previews?: string[]
  technicalMedia?: string[]
  notes?: string
  sources: LibrarySource[]
  price?: { amount: number; currency: string }
  tags: LibraryTag[]
  folders: string[]
}

export interface LibraryFilters { site: string; tag: string; folder: string }
export interface SavedView extends LibraryFilters { name: string }
export interface PrototypeLibrary {
  version: 1
  plugins: LibraryPlugin[]
  tags: LibraryTag[]
  folders: string[]
  sites: string[]
  views: SavedView[]
  // Preset views keep their meaning when the corresponding tag is renamed.
  presetTags: { purchased: string; wishlist: string[]; archived: string }
}
