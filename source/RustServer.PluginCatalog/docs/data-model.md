# RustServer.PluginCatalog — модель данных

**Статус:** Plugin хранит необязательные author, краткое description, Markdown sourceDescription, notes и price/currency; имя — единственное обязательное пользовательское поле. UUID и slug назначает backend. Существующие версии, связи и media сохраняются. Новая initial schema предназначена только для пустой БД; обновления существующих схем в этой задаче нет.

Каталог описывает известный плагин как продуктовую запись. Он не утверждает, что этот плагин установлен на сервере, загружен в Rust или применён через `RustServer.Plugins`.

## 1. Сущности и связи

```text
Plugin 1 ─── * PluginSource
   │
   └──── * PluginMedia
             ▲
Plugin.IconMediaId ─────┘   (0..1, главная иконка того же Plugin)
```

`Plugin` — корень каталожной записи. `PluginSource` и `PluginMedia` принадлежат конкретному `Plugin`; они не являются глобальными каталогами ссылок или файлов.

## 2. Plugin

| Поле | Значение и правило |
| --- | --- |
| `Id` | Стабильный переносимый `Guid`; PostgreSQL отображает его как UUID. ID создаётся приложением, а не provider-специфичным sequence. |
| `Name` | Отображаемое имя плагина. |
| `Slug` | Backend генерирует lowercase ASCII slug из имени; при коллизии добавляет UUID. Стабилен при `PUT`; PostgreSQL обеспечивает уникальность индексом. |
| `Description` | Необязательное краткое описание плагина. |
| `Author` | Необязательное имя автора. |
| `SourceDescription` | Необязательный длинный Markdown-текст с сайта. |
| `Notes` | Необязательные личные заметки. |
| `Price`, `Currency` | Необязательная неотрицательная цена и трёхбуквенная валюта; указываются вместе и не означают покупку. |
| `CurrentKnownVersion` | Последняя известная версия **в каталоге**. Это не установленная и не запущенная версия на игровом сервере. Значение может быть неизвестно. |
| `LastPublishedDate` | Дата публикации или последнего обновления у автора, если она известна. Это не дата изменения нашей записи. |
| `IconMediaId` | Необязательная ссылка на `PluginMedia`, выбранное главной иконкой. При создании Plugin иконка не требуется. |
| `CreatedAt` | Время создания записи в каталоге. |
| `UpdatedAt` | Время последнего изменения записи в каталоге. |

`LastPublishedDate` is a calendar date (`DateOnly?`, PostgreSQL `date`); `CreatedAt` and `UpdatedAt` are UTC timestamps and `UpdatedAt` cannot precede `CreatedAt`.

## 3. Site and PluginSource

Site is a managed directory with UUID, required Name and optional BaseUrl. PluginSource has an optional SiteId, its own URL and optional label. Deleting a Site clears its references and preserves sources. There is no fixed source kind.

Один `Plugin` может иметь несколько внешних ссылок. Минимальная запись источника содержит:

| Поле | Значение |
| --- | --- |
| `Id` | Стабильный переносимый `Guid`/UUID. |
| `PluginId` | Идентификатор владельца. |
| `SiteId` | Optional reference to Site. |
| `Url` | Адрес внешнего ресурса. Он не означает, что сервис уже умеет его обходить или скачивать с него данные. |
| `Label` | Optional display label. |

Source operations are scoped to the owning Plugin; a missing source returns `404`.

## 4. PluginMedia

`PluginMedia` описывает медиа, принадлежащее плагину. Байты не размещаются в БД.

| Поле | Значение |
| --- | --- |
| `Id` | Стабильный переносимый `Guid`/UUID. |
| `PluginId` | Идентификатор владельца. |
| `Kind` | Доменный enum, хранимый строкой: `Icon`, `Screenshot`, `AnimatedImage`, `Video`, `Preview` или `Technical`. Local upload разрешён для `Icon`, `Screenshot`, `AnimatedImage`, `Preview` и `Technical`; external URL — для `Video`, `Preview` и `Technical`. Старые значения сохраняются без преобразования. |
| `Caption` | Необязательная операторская подпись. |
| `SortOrder` | Явный порядок показа в галерее. |
| `StorageKey` | Необязательный относительный логический ключ оригинала в media storage. В Stage 05 имеет вид `{pluginId}/{mediaId}/original.{png|jpg|webp|gif}` **без** префикса `media/`; не является абсолютным путём, URL файловой системы или прямым S3 URL. |
| `ExternalUrl` | Необязательная абсолютная HTTP(S) ссылка на внешнее медиа, например видео или preview. Это не physical URL выбранного storage provider. |
| `ContentType` | Описывает сохранённый формат, когда в этапе 5 появится загрузка. |

Каждая `PluginMedia` имеет ровно один непустой источник: `StorageKey` **либо** `ExternalUrl`. PostgreSQL check constraint исключает пустое и двойное расположение. Варианты будущего original/thumbnail живут рядом в каталоге одного media; отдельная сущность или система трансформаций на этапе 3 не создаётся.

`IconMediaId`, если задан, связан составным FK `(Plugin.Id, IconMediaId) → (PluginMedia.PluginId, PluginMedia.Id)`: БД гарантирует принадлежность иконки тому же Plugin. Для этой ссылки установлен `Restrict`, а не cascade. Handler Stage 05 явно проверяет `Kind == Icon`; при удалении выбранной иконки сначала очищает `IconMediaId` в той же DB-транзакции, затем удаляет metadata. Обычным FK это правило выразить нельзя.

## 5. Намеренно отсутствующие данные

На этом этапе не создаются `PluginInstallations`, категории, авторы, история версий, журнал проверок версий или таблицы связей с сервером. Будущая связь с установленным плагином будет проектироваться отдельно на основе стабильных ID и явно согласованной стратегии сопоставления; нельзя неявно связывать её по одному имени или текущей версии.

## 6. Реализованные ограничения persistence

PostgreSQL adapter задаёт обязательность и максимальные размеры: `Name` 200, `Slug` 160, `Description` 8000, известная версия 100, URL 2048, label 200, caption 500, `StorageKey` 1024 и content type 255 символов. Реальные индексы: уникальный `Slug`, `(Kind, PluginId)` для фильтра источника и `(PluginId, SortOrder)` для упорядоченной галереи. Связи Plugin → Source/Media — ownership cascade; связь главной иконки — `Restrict`.

## 7. Media варианты и границы persistence

PostgreSQL через EF Core 10/Npgsql сохраняет только metadata. Local Filesystem adapter сохраняет original и детерминированный `thumbnail.webp` рядом с ним, не добавляя отдельную таблицу или StorageKey для thumbnail. HTTP DTO никогда не раскрывают `StorageKey`, physical path или provider-specific детали: вместо них возвращаются стабильные API URLs, построенные по ID. Модель не содержит provider-specific идентификаторов, абсолютных путей и BLOB. Это оставляет возможность переноса PostgreSQL → MS SQL Server и Local Filesystem → S3 без смены идентичности Plugin/Media или публичного DTO.

`PluginMedia` не предусматривает замену original: ID и URL immutable. Поэтому cache original/thumbnail может быть долгим и не нуждается в cache-busting версии. External media не имеет bytes, thumbnail или local content endpoint; `ExternalUrl` возвращается лишь как `accessUrl` отображения.

Rollout использует чистую БД и текущую initial schema. Подробности описаны в [`storage-and-migration.md`](storage-and-migration.md).

## Tags и Folders

`Tag` (UUID, name до 200, color `#RRGGBB`) и `Folder` (UUID, name до 200) — пользовательские независимые сущности. `plugin_tags` и `plugin_folders` хранят уникальные пары UUID с каскадным удалением связей. Удаление метки или папки не удаляет Plugin. Иерархии папок и фиксированных меток нет.

Site BaseUrl допускает null. JSON import создаёт UUID и slug на сервере и сопоставляет имена справочников без учёта регистра.

ZIP export сохраняет все UUID, атрибуты и связи каталога, включая версии как хранимые метаданные, и оригиналы локальных media. Внешние URL сохраняются без скачивания. StorageKey остаётся логическим относительным ключом; archivePath использует UUID media.
