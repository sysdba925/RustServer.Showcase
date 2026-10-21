# RustServer.PluginCatalog — REST API каталога

**Статус:** архитектурная коррекция этапа 8. Этот документ синхронизирован с Development OpenAPI `GET /openapi/v1.json`; Swagger UI не подключён.

## 1. Границы и общие правила

- API описывает записи **каталога**, а не установленный, загруженный или запущенный Rust-плагин. Git, Deploy, игровые файлы, Web UI, S3, MS SQL и DataMigration не входят в контракт.
- Базовый префикс — `/api`. Идентификаторы передаются как UUID. Все timestamps — ISO 8601 с offset (`date-time`) и представляют UTC-время, записанное backend.
- Успешные JSON-ответы используют camelCase. Ошибки имеют media type `application/problem+json` и machine-readable поле `code`.
- API не раскрывает `StorageKey`, physical path, connection string или provider-specific URL. `accessUrl` строится из стабильных API UUID; внешний URL возвращается только как `accessUrl` соответствующей external media.
- Аутентификация и авторизация пока не реализованы. Upload и управление каталогом **нельзя** публиковать как production-ready public API до согласованной надёжной authn/authz boundary.

## 2. Health

| Endpoint | Meaning | Response |
| --- | --- | --- |
| `GET /health` | Liveness only; no PostgreSQL or media-root I/O. | `200` while the process is alive. |
| `GET /health/ready` | Deployment readiness. It calls provider-neutral readiness probes for the configured Catalog database and media storage. PostgreSQL currently reads required tables; Local Filesystem checks an existing root with an auto-deleted probe file. | `200` when configured dependencies are usable; `503` otherwise. It never creates schema, media root, or Catalog data. |

`/health/ready` is an operational readiness endpoint, not a CRUD capability. It neither provisions nor migrates dependencies, and must not be used to trigger provisioning.

## 2. DTO

### PluginSummaryDto

| Поле | Тип | Значение |
| --- | --- | --- |
| `id` | UUID | Стабильный ID Plugin. |
| `name` | string | Отображаемое имя. |
| `slug` | string | Нормализованный lowercase идентификатор. |
| `shortDescription` | string | Краткое описание, обрезанное до 240 символов; пустая строка при отсутствии. |
| `description`, `author`, `notes`, `sourceDescription`, `currency` | string \| null | Необязательные поля карточки; `sourceDescription` — Markdown. |
| `price` | decimal \| null | Неотрицательная цена; наличие цены не означает покупку. |
| `currentKnownVersion` | string \| null | Известная в каталоге версия, не версия установленного Rust-плагина. |
| `lastPublishedDate` | date \| null | Календарная дата публикации `YYYY-MM-DD`. |
| `updatedAt` | date-time | Время изменения записи каталога. |
| `icon` | `PluginIconDto` \| null | Выбранная иконка или `null`. Список получает её одним paged DB query, без N+1. |

List summaries include `sources` as `PluginSourceDto[]`, ordered by UUID. Sources are loaded in bounded page-level queries without N+1 requests.

### PluginDetailsDto

Содержит все поля `PluginSummaryDto`, а также:

| Поле | Тип | Значение |
| --- | --- | --- |
| `description` | string \| null | Краткое описание. |
| `createdAt` | date-time | Время создания записи каталога. |
| `sources` | `PluginSourceDto[]` | Sources ordered by UUID. |
| `iconMediaId` | UUID \| null | Selected icon ID или `null`. |
| `media` | `PluginMediaDto[]` | Вся галерея, упорядоченная по `sortOrder`, затем UUID. |

### PluginIconDto

| Поле | Тип | Значение |
| --- | --- | --- |
| `id` | UUID | ID media с kind `Icon`. |
| `accessUrl` | string | Relative API URL original. |
| `thumbnailAccessUrl` | string | Relative API URL static WebP thumbnail. |
| `contentType` | string | Content type подтверждённого original. |

### PluginMediaDto

| Поле | Тип | Значение |
| --- | --- | --- |
| `id` | UUID | Стабильный ID media. |
| `kind` | string enum | `Icon`, `Screenshot`, `AnimatedImage`, `Video`, `Preview`, `Technical`. |
| `caption` | string \| null | Подпись оператора. |
| `sortOrder` | integer | Неотрицательный порядок в галерее. |
| `contentType` | string \| null | Подтверждённый тип local original; у external link — `null`. |
| `accessUrl` | string | Для local media — URL original; для external media — сохранённый absolute HTTP(S) URL. |
| `thumbnailAccessUrl` | string \| null | URL local static WebP thumbnail; external media не имеет thumbnail. |

### PluginSourceDto и PagedResultDto

`PluginSourceDto` contains `id`, optional `siteId`, `url` and optional `label`. `PagedResultDto<T>` contains `items`, `page`, `pageSize`, `totalItems`, `totalPages`.

## 3. Plugin и Source requests

`CreatePluginRequest` и `UpdatePluginRequest` содержат `name`, `author`, `description`, `sourceDescription`, `notes`, `price`, `currency`, `currentKnownVersion`, `lastPublishedDate`, `sources`, `tagIds`, `folderIds`. Единственное обязательное поле — непустое `name` (до 200 символов). `author` ограничен 200, `description` — 8000, `currentKnownVersion` — 100 символами; `notes` и Markdown `sourceDescription` не имеют заданного прикладного лимита. Цена и валюта задаются вместе: цена неотрицательная, не более двух знаков после запятой и 9999999999999999.99; валюта — три латинские буквы, сохраняется uppercase. `lastPublishedDate` — календарная дата `YYYY-MM-DD`. Каждый элемент `sources` содержит обязательный URL и необязательные `siteId` и `label`; все переданные ID должны существовать. Массивы заменяют соответствующие коллекции, отсутствие или null означает пустую коллекцию. PUT заменяет все поля, null очищает необязательные значения. Поля и связи сохраняются одним commit БД. Slug создаёт сервер из имени; при редактировании он остаётся стабильным.

Пример POST или PUT:

```json
{
  "name": "Example",
  "author": "Author",
  "currentKnownVersion": "1.2.3",
  "lastPublishedDate": "2026-10-18",
  "sources": [{ "url": "https://example.org/plugin", "siteId": null, "label": "Home" }],
  "tagIds": [],
  "folderIds": []
}
```

`CreateSourceRequest` and `UpdateSourceRequest` contain optional `siteId`, required absolute HTTP(S) `url` up to 2048 characters and optional `label` up to 200 characters.

`GET /api/plugins` accepts `page`, `pageSize`, `search`, and optional `siteId`. Search matches name, slug, description, or author case-insensitively. Filtering, total, deterministic sorting (`name ASC`, `id ASC`) and pagination execute in DB; the site filter uses existence and does not duplicate plugins.

`GET /api/sites`, `GET /api/sites/{id}`, `POST /api/sites`, `PUT /api/sites/{id}` and `DELETE /api/sites/{id}` manage sites. Requests contain required `name` and optional `baseUrl`; responses also include `id`. Unknown sites return `404 site_not_found`, including when referenced by a source. Deletion clears source references and preserves sources.

## 4. Media requests, upload и content

### Multipart `POST /api/plugins/{pluginId}/media`

`multipart/form-data` содержит один обязательный part `file`, обязательный `kind`, optional `caption` и required `sortOrder` (integer `>= 0`). Local upload разрешает kind `Icon`, `Screenshot`, `AnimatedImage`, `Preview` и `Technical` и только PNG, JPEG, WebP или GIF. Client filename, extension и declared `Content-Type` не являются источником истины.

Backend применяет согласованный 10 MiB multipart/request limit; format определяет magic-byte whitelist и полная decode-проверка. Также разрешены не более 25,000,000 decoded pixels и 300 frames. `original` сохраняется без изменений, а рядом генерируется static `thumbnail.webp` (для GIF — первый frame). Успешный ответ — `201 PluginMediaDto`.

### JSON external media

`POST /api/plugins/{pluginId}/media/external` принимает JSON:

```json
{
  "kind": "Video",
  "externalUrl": "https://example.test/preview",
  "caption": "Optional label",
  "sortOrder": 0
}
```

Разрешены только kind `Video`, `Preview` или `Technical`, non-empty absolute HTTP(S) `externalUrl`, caption до 500 символов и non-negative `sortOrder`. Endpoint не скачивает, не проверяет доступность, не проксирует и не запрашивает этот URL server-side. Успех — `201 PluginMediaDto` с external `accessUrl` и null `contentType`/`thumbnailAccessUrl`.

### Metadata, icon и bytes

`PUT /api/plugins/{pluginId}/media/{mediaId}` принимает JSON `{ "caption": string|null, "sortOrder": integer }`; он меняет только metadata, не заменяет bytes и ID. `PUT /api/plugins/{pluginId}/icon` принимает `{ "mediaId": UUID|null }`: null снимает selection, иначе media обязано принадлежать route plugin и иметь kind `Icon`.

`GET /api/plugins/{pluginId}/media/{mediaId}/content` отдаёт original local bytes с сохранённым `Content-Type`. Optional exact query `variant=thumbnail` отдаёт `image/webp`; любые другие значения — `400`. External media не имеет content endpoint и возвращает `409`. Original и thumbnail immutable по UUID и содержат `Cache-Control: public, max-age=31536000, immutable`.

## 5. Маршруты

| Метод и маршрут | Успех | Ошибки |
| --- | --- | --- |
| `GET /api/plugins` | `200 PagedResultDto<PluginSummaryDto>` | `400 validation_failed` |
| `POST /api/plugins` | `201 PluginDetailsDto` | `400 validation_failed`, `409 slug_conflict` |
| `GET /api/plugins/{pluginId}` | `200 PluginDetailsDto` | `404 plugin_not_found` |
| `PUT /api/plugins/{pluginId}` | `200 PluginDetailsDto` | `400 validation_failed`, `404 plugin_not_found`, `409 slug_conflict` |
| `DELETE /api/plugins/{pluginId}` | `204` | `404 plugin_not_found` |
| `GET`, `POST /api/plugins/{pluginId}/sources` | `200 PluginSourceDto[]` / `201 PluginSourceDto` | `400 validation_failed`, `404 plugin_not_found` |
| `PUT`, `DELETE /api/plugins/{pluginId}/sources/{sourceId}` | `200 PluginSourceDto` / `204` | `400 validation_failed`, `404 source_not_found` |
| `GET /api/plugins/{pluginId}/media` | `200 PluginMediaDto[]` | `404 plugin_not_found` |
| `POST /api/plugins/{pluginId}/media` | `201 PluginMediaDto` | `400 validation_failed`, `404 plugin_not_found`, `413 upload_too_large`, `415 unsupported_media_type`, `503 media_storage_unavailable` |
| `POST /api/plugins/{pluginId}/media/external` | `201 PluginMediaDto` | `400 validation_failed`, `404 plugin_not_found` |
| `PUT /api/plugins/{pluginId}/media/{mediaId}` | `200 PluginMediaDto` | `400 validation_failed`, `404 media_not_found` |
| `DELETE /api/plugins/{pluginId}/media/{mediaId}` | `204` | `404 media_not_found` |
| `PUT /api/plugins/{pluginId}/icon` | `200 PluginDetailsDto` | `400 validation_failed`, `404 plugin_not_found`/`media_not_found`, `409 media_is_not_icon` |
| `GET /api/plugins/{pluginId}/media/{mediaId}/content` | bytes original/thumbnail | `400 validation_failed`, `404 media_not_found`, `409 external_media_has_no_content`, `503 media_content_unavailable` |

Ownership is always checked. Deleting selected icon clears its selection before media metadata deletion. `DELETE Plugin` removes its sources, associations and media metadata in a database transaction, then removes local media files. Cleanup failures are logged as orphaned files; the successful deletion is not rolled back. Rust runtime files are not touched.

## 6. Errors

Every error is RFC 9457 Problem Details with `code`. Published codes are `validation_failed`, `plugin_not_found`, `source_not_found`, `media_not_found`, `slug_conflict`, `media_is_not_icon`, `external_media_has_no_content`, `upload_too_large`, `unsupported_media_type`, `media_storage_unavailable`, `media_content_unavailable`, `tag_not_found`, `folder_not_found`, `invalid_json` and `internal_error`. Development OpenAPI explicitly models the string `code` extension. SQL exception text, constraint names, connection strings and local paths are never exposed.

## Tags и Folders

`GET/POST /api/tags` и `GET/PUT/DELETE /api/tags/{id}` управляют метками `{id,name,color}`; аналогичные маршруты `/api/folders` управляют папками `{id,name}`. Name после trim обязателен, до 200 символов; color обязателен в формате `#RRGGBB`. Удаление удаляет связи, но не плагины. POST возвращает 201 с DTO и Location, GET/PUT — 200 с DTO, DELETE — 204; назначение и снятие связи возвращают 204. Списки сортируются по имени и UUID. Повторное назначение и удаление отсутствующей связи идемпотентны. `PUT/DELETE /api/plugins/{pluginId}/tags/{id}` и `/folders/{id}` назначают и снимают связь; отсутствующий plugin или объект возвращает 404. List и Details содержат массивы `tags` и `folders`. `GET /api/plugins` принимает необязательные UUID `tagId` и `folderId`; оба фильтра совместно с остальными имеют семантику AND и применяются до подсчёта и пагинации. Цена не создаёт метку покупки.

## JSON import v2

`POST /api/import/preview` и `POST /api/import/commit` принимают `{ "schemaVersion": 2, "plugins": [...] , "createMissing": false }`. `createMissing` — необязательное подтверждение создания неизвестных Sites, Tags и Folders; без него commit с неизвестными именами отклоняется. Preview возвращает записи с индексом, нормализованным именем, ошибками, совпадениями и списками неизвестных имён. Сравнение имён без учёта регистра и пробелов по краям, URL после нормализации HTTP(S). Совпадения по имени или URL пропускаются, существующие записи не обновляются; совпадения внутри файла также пропускаются. Commit заново проверяет весь документ, при ошибке ничего не сохраняет; результат содержит число созданных и пропущенных записей. Неизвестные поля запрещены. `currentKnownVersion` и `lastPublishedDate` необязательны; установка и медиа не импортируются.

Site `baseUrl` необязателен и может быть null. Rollout использует чистую БД; старые схемы не поддерживаются.

## Полный ZIP export v2

`GET /api/export` возвращает `application/zip` (`plugin-catalog.zip`) после полной сборки архива. `manifest.json` содержит `formatVersion: 2`; `catalog.json` содержит массивы `plugins`, `sites`, `tags`, `folders`, `sources`, `media`. Записи сохраняют UUID, поля модели и связи: у plugin — `tagIds`, `folderIds`, `iconMediaId`, у source и media — `pluginId`, у source — `siteId`. Локальная media содержит переносимый `storageKey` и `archivePath` вида `media/{mediaId}`; в архиве хранится оригинал. Внешняя media содержит `externalUrl`, без загрузки. Thumbnail не включён. При отсутствии или ошибке чтения оригинала API возвращает 503 и не выдаёт архив. Экспорт не включает конфигурацию, секреты или физические пути. Это формат резервного экспорта, отличный от JSON import.

`POST /api/restore/preview` принимает тело `application/zip` формата v2 (не multipart), максимум 64 MiB в сжатом виде, 256 MiB распакованных данных, 16 MiB на каждый JSON и 10 000 записей ZIP. Ответ 200 содержит `counts` по шести коллекциям и `conflicts`: тип, UUID архивной записи и причина (`id` или `slug`). Сравнение выполняется со снимком текущего каталога; результат может устареть при параллельных изменениях. Некорректный, неполный или превышающий внутренние лимиты архив возвращает 400 Problem Details; сервер может отклонить слишком большое HTTP-тело с 413, неверный тип — 415. Проверяются версия, структура, UUID, ссылки, уникальность идентификаторов и наличие и читаемость оригиналов. Пути не извлекаются на диск. Preview не изменяет БД или media storage.

`POST /api/restore/commit` принимает такой же ZIP и заново выполняет проверку preview. Успех возвращает 200 с `counts`; непустой каталог или уже существующий original/thumbnail — 409. Метаданные, UUID, связи и ключи сохраняются; файлы создаются без перезаписи. PostgreSQL блокирует таблицы каталога на время проверки пустоты и транзакции, а при сбое новые файлы удаляются. Thumbnail в экспорте отсутствует; при Restore он создаётся из оригинала тем же механизмом, что при загрузке. БД и файловая система не имеют общей транзакции: аварийное завершение процесса либо неопределённый исход commit БД может потребовать операторской проверки и очистки осиротевших файлов. При неудачной очистке возвращается ошибка сервера; существующие файлы не удаляются.

JSON import v2 также принимает необязательные `plugins[].sourceDescription` (Markdown) и `plugins[].sources[].label` (до 200 символов). Preview проверяет label по тем же правилам, что обычный Source API.
