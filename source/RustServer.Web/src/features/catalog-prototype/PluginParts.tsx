import type { LibraryPlugin, LibrarySource, LibraryTag } from './model'
import styles from './prototype.module.css'

export function TagList({ tags }: { tags: LibraryTag[] }) {
  return tags.length ? <div className={styles.tags}>{tags.map(tag => <span className={styles.tag} style={{ color: tag.color, borderColor: tag.color }} key={tag.name}>{tag.name}</span>)}</div> : <span className={styles.muted}>No tags</span>
}

export function SourceList({ sources }: { sources: LibrarySource[] }) {
  return sources.length ? <div className={styles.sources}>{sources.map((source, index) => <a key={`${source.url}-${index}`} href={source.url} target="_blank" rel="noreferrer">{source.site || 'Website'} ↗</a>)}</div> : <span className={styles.muted}>No sources</span>
}

export function Price({ price, tags = [] }: { price?: LibraryPlugin['price']; tags?: LibraryTag[] }) {
  const names = tags.map(tag => tag.name.toLowerCase())
  const color = names.some(name => name === 'pirated' || name === 'unlicensed' || name === 'pirated / unlicensed') ? styles.priceRed : names.includes('purchased') ? styles.priceGreen : names.includes('planned purchase') ? styles.priceYellow : ''
  return price ? <span className={color}>{new Intl.NumberFormat('en', { style: 'currency', currency: price.currency }).format(price.amount)}</span> : <span className={styles.muted}>—</span>
}

export function PluginIcon({ plugin }: { plugin: LibraryPlugin }) {
  const initials = plugin.name.split(' ').map(part => part[0]).slice(0, 2).join('')
  return plugin.iconUrl ? <img className={styles.icon} src={plugin.iconUrl} alt="" /> : <span className={styles.icon} aria-hidden="true"><svg viewBox="0 0 48 48" width="100%" height="100%"><path d="M0 39 16 22l10 8L48 6v42H0Z" fill="#475d37" /><path d="M0 7 17 0h31v8L25 25Z" fill="#52683e" /><text x="24" y="31" textAnchor="middle" fill="#e0e9c8" fontSize="15" fontWeight="bold">{initials}</text></svg></span>
}

function Identity({ plugin, onOpen }: { plugin: LibraryPlugin; onOpen?: () => void }) {
  return <div className={styles.identity}>{onOpen ? <button className={styles.iconButton} type="button" onClick={onOpen} aria-label={`Open ${plugin.name}`}><PluginIcon plugin={plugin} /></button> : <PluginIcon plugin={plugin} />}<div>{onOpen ? <button type="button" className={styles.nameButton} onClick={onOpen}>{plugin.name}</button> : <strong>{plugin.name}</strong>}<div className={styles.muted}>{plugin.author ?? 'Unknown author'}</div></div></div>
}

export function PluginRow({ plugin, onOrganize, onOpen }: { plugin: LibraryPlugin; onOrganize?: () => void; onOpen?: () => void }) {
  return <tr><td><Identity plugin={plugin} onOpen={onOpen} /></td><td><SourceList sources={plugin.sources} /></td><td><TagList tags={plugin.tags} /></td><td><Price price={plugin.price} tags={plugin.tags} /></td><td className={styles.muted}>{plugin.folders.join(', ') || '—'}</td>{onOrganize && <td><button className={styles.organizeButton} type="button" onClick={onOrganize} aria-label={`Organize ${plugin.name}`}>Organize</button></td>}<td>{plugin.lastUpdate || '—'}</td></tr>
}

export function PluginCard({ plugin, onOrganize, onOpen }: { plugin: LibraryPlugin; onOrganize?: () => void; onOpen?: () => void }) {
  return <article className={styles.card}><div className={styles.cardHeading}><Identity plugin={plugin} onOpen={onOpen} />{onOrganize && <button className={styles.organizeButton} type="button" onClick={onOrganize} aria-label={`Organize ${plugin.name}`}>Organize</button>}</div><p className={styles.cardDescription}>{plugin.description ?? 'No description yet.'}</p><TagList tags={plugin.tags} /><div className={styles.cardFooter}><SourceList sources={plugin.sources} /><strong><Price price={plugin.price} tags={plugin.tags} /></strong></div><div className={styles.cardFolders}>Folders: {plugin.folders.join(', ') || '—'} · Last update: {plugin.lastUpdate || '—'}</div></article>
}
