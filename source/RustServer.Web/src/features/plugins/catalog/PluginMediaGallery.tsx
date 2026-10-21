import { useState } from 'react'

import { resolvePluginCatalogContentUrl, type PluginMediaDto } from '../../../shared/api'
import styles from './PluginDetailsPage.module.css'

interface PluginMediaGalleryProps {
  media: PluginMediaDto[]
  iconMediaId: string | null
  pending: boolean
  onAddExternal: () => void
  onClearIcon: () => void
  onDelete: (media: PluginMediaDto) => void
  onEdit: (media: PluginMediaDto) => void
  onPreview: (media: PluginMediaDto) => void
  onSetIcon: (mediaId: string) => void
  onUpload: () => void
}

function MediaThumbnail({ media }: { media: PluginMediaDto }) {
  const [failed, setFailed] = useState(false)

  if (media.thumbnailAccessUrl === null) {
    return <div aria-label={`${media.kind} external media`} className={styles.externalPlaceholder} role="img">↗</div>
  }

  return failed ? (
    <div aria-label={`${media.caption ?? media.kind} unavailable`} className={styles.imageFallback} role="img">⬡</div>
  ) : (
    <img
      alt={media.caption ?? `${media.kind} media`}
      className={styles.thumbnail}
      loading="lazy"
      onError={() => setFailed(true)}
      src={resolvePluginCatalogContentUrl(media.thumbnailAccessUrl)}
    />
  )
}

/** Ordered Catalog gallery; its caller passes media in backend response order. */
export function PluginMediaGallery({
  media,
  iconMediaId,
  pending,
  onAddExternal,
  onClearIcon,
  onDelete,
  onEdit,
  onPreview,
  onSetIcon,
  onUpload,
}: PluginMediaGalleryProps) {
  return (
    <section aria-labelledby="media-heading" className={styles.section}>
      <div className={styles.sectionHeader}>
        <div>
          <h2 id="media-heading">Media gallery</h2>
          <p>Images are stored by Catalog; external Video, Preview and Technical entries remain external links.</p>
        </div>
        <div className={styles.sectionActions}>
          <button className={styles.secondaryButton} disabled={pending} onClick={onAddExternal} type="button">Add external media</button>
          <button className={styles.primaryButton} disabled={pending} onClick={onUpload} type="button">Upload image</button>
        </div>
      </div>
      {iconMediaId !== null && (
        <div className={styles.iconSelection}>
          <p>One local Icon is selected for this plugin and the catalog list.</p>
          <button className={styles.secondaryButton} disabled={pending} onClick={onClearIcon} type="button">Clear icon</button>
        </div>
      )}
      {media.length === 0 ? (
        <p className={styles.empty}>No media has been added.</p>
      ) : (
        <ul className={styles.gallery}>
          {media.map((item) => {
            const selectedIcon = item.id === iconMediaId
            const isExternal = item.thumbnailAccessUrl === null
            return (
              <li key={item.id}>
                <article className={styles.mediaCard}>
                  <MediaThumbnail media={item} />
                  <div className={styles.mediaBody}>
                    <div className={styles.mediaHeading}>
                      <span className={styles.kind}>{item.kind}</span>
                      {selectedIcon && <span className={styles.selected}>Selected icon</span>}
                    </div>
                    <p>{item.caption ?? 'No caption'}</p>
                    <small>Order {item.sortOrder}</small>
                    {isExternal ? (
                      <a className={styles.externalLink} href={item.accessUrl} rel="noopener noreferrer" target="_blank">Open external link</a>
                    ) : (
                      <button className={styles.linkButton} disabled={pending} onClick={() => onPreview(item)} type="button">Open original</button>
                    )}
                    <div className={styles.rowActions}>
                      {item.kind === 'Icon' && !selectedIcon && <button className={styles.secondaryButton} disabled={pending} onClick={() => onSetIcon(item.id)} type="button">Set as icon</button>}
                      <button className={styles.secondaryButton} disabled={pending} onClick={() => onEdit(item)} type="button">Edit</button>
                      <button className={styles.dangerButton} disabled={pending} onClick={() => onDelete(item)} type="button">Delete</button>
                    </div>
                  </div>
                </article>
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
