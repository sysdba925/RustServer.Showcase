# Plugin Catalog prototype backup v1

This is a browser prototype format, not a published backend contract. It is a
full snapshot, distinct from the JSON collection import. Restoration replaces
the current browser library after preview and confirmation.

- `manifest.json` contains `format` (`rustserver.plugin-catalog.backup`),
  `formatVersion` (1), `createdAt`, the SHA-256 of `catalog.json`, media
  associations, the number of external URLs, and unavailable local references.
- `catalog.json` contains the complete prototype library, including plugins,
  sources, tags, folders, saved views, settings and their relationships. An
  included local image is represented by `archive:media/NNNNNN`.
- `media/NNNNNN` contains image bytes. Each manifest entry records its path,
  SHA-256, MIME type, plugin ID, purpose (`Icon`, `Preview`, `Technical`) and
  position within that purpose. External URLs remain unchanged in the catalog.

The prototype embeds available PNG, JPEG, GIF and WebP data/blob images. It
never fetches HTTP(S) images for backup. Missing or unsupported local media is
listed explicitly in the manifest and preview, and its original reference is
retained; such references may not work after restoration. Restored local images
are stored as data URLs and are subject to browser localStorage limits. Archive
size is limited to 20 MiB compressed and 24 MiB expanded. This format requires
an explicit backend design before it can be treated as a backend backup format.
