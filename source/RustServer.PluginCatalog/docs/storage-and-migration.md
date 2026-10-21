# RustServer.PluginCatalog — storage и перенос данных

**Статус:** архитектурная коррекция этапа 8. PostgreSQL metadata mapping и Local Filesystem adapter выбираются через configuration/DI. Catalog release artifact сам содержит раздельные provisioning tools; S3 adapter и инструмент переноса пока не реализуются.

## 1. Разделение хранения

Первая реализация будет разделять два вида данных:

| Данные | Первое хранилище | Что сохраняется |
| --- | --- | --- |
| Каталожные метаданные | PostgreSQL | Plugin, PluginSource, PluginMedia, стабильные UUID, подписи, порядок, `StorageKey` или `ExternalUrl` |
| Бинарные медиа | Local Filesystem | Оригиналы изображений/GIF и будущие производные варианты, например thumbnail |

Бинарные изображения и GIF **не хранятся BLOB в БД**. В БД не помещаются абсолютный путь, URL локального файла и прямой физический URL S3.

## 2. StorageKey и Local Filesystem

`StorageKey` — переносимый относительный логический ключ. Он определяет местоположение внутри выбранного media storage, но не знает его root и provider. Пример целевого расположения первого adapter:

```text
/var/lib/rustserver/plugin-catalog/media/
  {pluginId}/
    {mediaId}/
      original.png
      thumbnail.webp
```

В Stage 05 `StorageKey` всегда соответствует части `{pluginId}/{mediaId}/original.{extension}` (то есть **не** включает префикс `media/`); конкретное расширение определяется подтверждённым форматом. `thumbnail.webp` — детерминированный sibling и не имеет отдельного primary key. Путь выше — постоянные данные **вне release-каталога** приложения. Его нельзя выводить из current release, working directory, develop checkout или физического URL.

HTTP API строит URL доступа из идентификаторов и сервисной конфигурации. Он не передаёт клиенту `StorageKey` как разрешение на чтение произвольного файла.

Для внешнего видео/preview `PluginMedia.ExternalUrl` хранит абсолютный HTTP(S) URL, а `StorageKey` остаётся `null`. Для локально загруженного файла наоборот: `StorageKey` задан, `ExternalUrl` отсутствует. External URL не скачивается, не проксируется и не используется для server-side fetch. Thumbnail располагается рядом с оригиналом и не требует отдельного ключа/модели.

`MediaStorage:RootPath` — явная конфигурация adapter. Поставляемый Catalog `media` provisioning создаёт production root `/var/lib/rustserver/plugin-catalog/media/`; Development задаёт отдельный локальный root. Оба пути вне release/checkouts. Adapter не вычисляет root от assembly, release или checkout. Перед каждым чтением/записью он проверяет provider/options и доступность каталога, безопасно соединяет только validated portable key, отвергает `..`, absolute paths, backslash и любые existing symlink/reparse-point components. Root намеренно не создаётся при API startup или `/health`; `/health/ready` проверяет уже provisioned root без сохранения данных.

Upload сначала копируется с лимитом во временный файл под root. После magic-byte whitelist и полной decode-проверки PNG/JPEG/WebP/GIF adapter генерирует static WebP thumbnail (для GIF — из первого кадра) во временный файл и атомарно перемещает оба артефакта в final directory. Original не перекодируется. Лимиты по умолчанию: upload 10 MiB, суммарно 25,000,000 decoded pixels и 300 frames; HTTP multipart/body limits согласованы с upload limit. Эти лимиты защищают от неограниченного чтения и decompression bomb, но не являются заменой auth/authz boundary.

## 3. Резервирование и жизненный цикл

PostgreSQL, media root и конфигурация должны резервироваться независимо друг от друга, а также независимо от release-артефактов приложения и игровых snapshots. Резервная копия считается полезной только при возможности согласованно восстановить метаданные и файлы с теми же стабильными ID и `StorageKey`.

Catalog release artifact содержит три независимые явно запускаемые операции: `host`, `database` и `media`. `host` создаёт service account, configuration/release roots, systemd и Nginx integration, но не создаёт database/schema или media root. Platform создаёт только root-owned generic Nginx include directory `/etc/rustserver/nginx/applications.d/`; `host` проверяет этот contract и устанавливает туда свой Admin-gated Catalog snippet. `database` читает `Database:Provider`, сейчас поддерживает только PostgreSQL, устанавливает/подготавливает локальный PostgreSQL, create-only role/database и initial schema для новой empty dedicated DB. Existing schema проверяется read-only; несовместимость останавливает операцию. `media` читает `MediaStorage:Provider`, сейчас поддерживает только Local, и create-only готовит root. Они не вызывают друг друга и никогда не запускаются API startup, release install/update, systemd restart или readiness.

Повторный запуск не удаляет и не пересоздаёт существующие роли, БД, схемы или files. Нет `EnsureCreated` production, auto-migrate, drop/recreate или изменения чужой database. `RustServer.Platform` и Updater не получают Catalog-specific connection string, provider, schema или root. Backup jobs, PostgreSQL→MS SQL и Local→S3 migration остаются отдельными будущими решениями.

Initial release installation follows a strict order: Platform Bootstrap →
Updater `Prepare` → explicit Catalog `host`/`database`/`media` → Updater
`Install`. Prepare verifies/stages the artifact and uses the generic root
Executor to re-verify and hand off a link-free copy into the root-owned,
non-group/world-writable `/opt/rustserver/plugin-catalog/prepared/<tag>/`
area. The handoff does not activate the release or run provisioning; Catalog
root commands reject mutable staging and run only from this prepared copy.
Update and Rollback never call a provisioning operation. Database SQL is read
by the privileged provisioner and streamed to the peer `psql` stdin, so the
service account does not need access to the protected artifact. The `media`
operation writes Catalog-managed systemd `ReadWritePaths` from the configured
Local root; changing that explicit root requires rerunning `media`, preserves
the former files and never relies on the default `/var/lib` path. `media` must
also run before first activation when an existing Local root needs no creation,
because that independent operation owns the systemd sandbox exception. A host created by the earlier
Platform-managed Catalog integration is migrated conservatively: Catalog host
provisioning accepts only the known legacy unit, saves a root-owned timestamped
backup and preserves its database, role and media values in Catalog-owned
configuration; an unknown unit stops for manual review. No database/media
state is deleted, reset or migrated automatically.

Upload выполняется в порядке `storage → DB metadata`; при неудаче записи metadata handler пытается удалить только что опубликованные original/thumbnail и логирует неудачный cleanup. Delete сначала commits DB metadata (одновременно снимая selected icon), затем удаляет files. Поэтому DB никогда не указывает на уже удалённые bytes; при filesystem error допустим orphan directory. Его можно безопасно повторно удалить тем же StorageKey через операционную процедуру; автоматический scan/job отсутствует на этом этапе.

Original и thumbnail immutable по media UUID. Endpoint content отдаёт `Cache-Control: public, max-age=31536000, immutable`; новый файл требует нового media ID, поэтому CDN/browser не видит старую версию как замену. Отдельный CDN или background worker не нужен.

## PostgreSQL Development schema

Rollout uses an empty PostgreSQL database and `initial-schema-current.sql`. `last_published_date` is a calendar date. Old schemas are not supported or migrated. Provisioning never migrates an existing schema.

## 4. Явное создание Development-схемы

The current initial schema includes Sites and nullable source SiteId with `ON DELETE SET NULL`. Rollout uses a clean Development database.

API никогда не вызывает `EnsureCreated` при старте. Для старых development БД есть небольшой console tool `tools/RustServer.PluginCatalog.DevSchema`.

Он вызывает `EnsureCreatedAsync` без истории и предназначен для пустой выделенной тестовой Development БД. `EnsureCreated` не является механизмом обновления.

## 5. Выбор provider и будущие adapters

Бизнес-логика зависит от port для persistence метаданных и port для media storage, а не от PostgreSQL или Local Filesystem напрямую. Composition root выбирает implementation только по обязательным `Database:Provider` и `MediaStorage:Provider`; missing/unsupported value is a startup/provisioning error and never falls back to a default. Сейчас точные supported values — `PostgreSql` and `Local` respectively.

| Граница | Первая реализация | Будущее, которое остаётся возможным |
| --- | --- | --- |
| Persistence | PostgreSQL | PostgreSQL → MS SQL Server или отдельный database API/service adapter |
| Media storage | Local Filesystem | Local Filesystem → S3 или отдельный Storage API/service adapter |

Сейчас не создаются дополнительные микросервисы и не пишутся adapters для MS SQL/S3. Их существование в таблице не является обязательством преждевременной реализации.

## 6. Future Portable Data Migration

В будущем потребуется самостоятельный `RustServer.DataMigration` (возможно CLI), который переносит **логический каталог вместе с медиа**, а не просто выполняет `pg_dump`. Целевые направления:

- PostgreSQL → PostgreSQL;
- PostgreSQL → MS SQL Server;
- Local Filesystem → S3;
- S3 → Local Filesystem или другой S3.

Предполагаемый переносимый пакет содержит manifest с версией формата и checksum, JSON-данные каталога и папку медиа. Он сохраняет `PluginId`, `MediaId` и `StorageKey`; не должен заменять их provider-specific идентификаторами.

Целевой процесс:

```text
Export → Import → Validate → Switch → Rollback
```

1. **Export** создаёт согласованный логический набор метаданных и медиа.
2. **Import** помещает набор в новую БД и storage, не объявляя их активными.
3. **Validate** проверяет manifest, версии формата, полноту ссылок, checksum и доступность медиа.
4. **Switch** переключает конфигурацию только после успешной проверки.
5. **Rollback** возвращает предыдущее рабочее подключение, если импорт или переключение не прошли.

На этапе 3 это только требование к будущей совместимости модели. Инструмент, format implementation, adapters MS SQL/S3, экспорт и импорт не создаются.

Текущая initial schema включает tags, folders и таблицы связей и применяется только к пустой БД.

Site BaseUrl в текущей initial schema допускает null.

ZIP export v2 содержит каталог и оригиналы, но не настройки и не физические пути. Restore commit доступен только для пустого каталога; автоматическая миграция отсутствует. Снимок метаданных и media не блокирует параллельные изменения; при исчезновении файла экспорт завершается ошибкой, поэтому для операционного согласованного бэкапа остановите изменения каталога на время экспорта.

## First-install diagnostic contract

When `RUSTSERVER_PROVISIONING_DIAGNOSTIC` is supplied by the Updater Executor, the Catalog install hook writes a bounded version 1 JSON record containing fixed non-secret stage, category and reason identifiers on failure. Host, database and media are distinct stages. PostgreSQL and other external command errors are classified by Catalog without embedding their output or configuration in that JSON record. The hook and its subprocesses retain their normal stdout/stderr; the Updater captures bounded output separately and exposes it through its operation API under the existing Emergency access boundary. Standalone provisioning and the protected artifact boundary are unchanged.

The PostgreSQL existing-schema check is also exercised against a freshly provisioned schema and repeated with existing data; retrying a partially completed first install validates the existing schema without recreating it.
