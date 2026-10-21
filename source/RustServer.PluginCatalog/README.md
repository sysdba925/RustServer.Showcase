# RustServer.PluginCatalog

`RustServer.PluginCatalog` — независимый API каталога известных Rust-плагинов. Catalog полностью владеет выбором, конфигурацией и explicit provisioning своих database/media dependencies; Platform и Updater не знают, используется PostgreSQL или MS SQL Server, Local Filesystem или S3. API не управляет Git, deploy, игровыми файлами или жизненным циклом Rust Server.

## Требования

- .NET SDK `10.0` (`net10.0`);
- NuGet-доступ для восстановления ASP.NET Core, EF Core/Npgsql и Magick.NET Q8;
- PostgreSQL для текущего PostgreSql adapter и явной инициализации выделенной development-схемы;
- persistent writable directory для media в окружениях, где выполняется upload/read.

Magick.NET Q8 используется только для подтверждения PNG/JPEG/WebP/GIF и создания thumbnail. Пакет Apache-2.0, переносим на Windows/Linux и включает native binaries; S3 client, MS SQL и отдельный storage service не добавлены.

## Команды

Из корня репозитория:

```powershell
dotnet restore RustServer.PluginCatalog.sln
dotnet build RustServer.PluginCatalog.sln
dotnet test RustServer.PluginCatalog.sln
```

Локальный Development запуск на loopback:

```powershell
dotnet run --project src/RustServer.PluginCatalog.Api --launch-profile http
```

Профиль слушает `http://localhost:5180` и не определяет production deployment. `GET /health` — только liveness: он не создаёт `DbContext`, не требует database и не создаёт/checks media root. `GET /health/ready` вызывает provider-neutral probes выбранных database и media storage; PostgreSQL читает требуемые таблицы, а Local проверяет запись автоудаляемого probe в уже provisioned root. Endpoint не создаёт schema, root или Catalog data и возвращает `503` при недоступной dependency. `GET /openapi/v1.json` доступен только в `Development`.

`Database:Provider` и `MediaStorage:Provider` выбирают adapters через DI. Current supported values are exactly `PostgreSql` and `Local`; unsupported or missing values fail clearly during startup/provisioning and never fall back. `Program.cs` and readiness use provider-neutral ports.

## Development PostgreSQL

Для пустой специально выделенной development DB явно создайте текущую схему:

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ConnectionStrings__PluginCatalog = '<dedicated-dev-database-connection-string>'
dotnet run --project tools/RustServer.PluginCatalog.DevSchema
```

`DevSchema` использует `EnsureCreated` и не записывает историю. Он предназначен только для пустой выделенной тестовой БД. Схема старого baseline v1 не содержит новых полей; обновление существующей БД в этой задаче не реализовано. Подробности в [storage-and-migration.md](docs/storage-and-migration.md).

## Local media storage

`MediaStorage:Provider=Local` — единственная работающая реализация. Root всегда задаётся конфигурацией, а не выводится из release/assembly:

```json
"MediaStorage": {
  "Provider": "Local",
  "RootPath": ".local/plugin-catalog/media",
  "MaxUploadBytes": 10485760,
  "MaxDecodedPixels": 25000000,
  "MaxFrameCount": 300,
  "ThumbnailMaxDimension": 480
}
```

Development default находится в игнорируемом `.local/`. Для другой local directory задайте, например:

```powershell
$env:MediaStorage__RootPath = (Join-Path (Get-Location) '.local/plugin-catalog/media')
```

Catalog host provisioning configures `/var/lib/rustserver/plugin-catalog/media/` outside release trees and API loopback `127.0.0.1:5116`. Platform owns only the root-owned Nginx application include point; the Catalog host template installs its own Admin-protected `/plugin-catalog-api/` snippet there.

Local adapter принимает только PNG, JPEG, WebP и GIF. Он не доверяет filename/extension/client content type: ограниченно копирует upload во временный файл, проверяет signature и полную decode-проверку, отклоняет более 10 MiB, 25,000,000 decoded pixels или 300 frames. Original сохраняется без перекодирования, рядом генерируется static `thumbnail.webp` (для GIF — первый frame). Storage key внутренний и не публикуется: `{pluginId}/{mediaId}/original.{confirmed-extension}` без префикса `media/`.

Adapter отвергает absolute/backslash/traversal paths и existing symlink/reparse-point components. Upload publishes temporary files atomically where possible. Metadata commit follows file creation; on DB failure handler attempts cleanup. Delete commits metadata (и clears selected icon) before physical files; a failed physical deletion is logged as an orphan and never leaves a DB link to deleted bytes.

Original и thumbnail immutable по media UUID и отдаются с `Cache-Control: public, max-age=31536000, immutable`. Original replacement отсутствует: для новой версии создаётся новое media. Это исключает stale cache без отдельного CDN/job.

## Production provisioning from a verified artifact

The release contains `provisioning/provision.sh`, owned by PluginCatalog. The Updater's
generic, explicitly registered **first-install hook** runs its `install` command as
root, from the root-owned artifact produced by **Updater Prepare**. In that one
operation PluginCatalog runs host → database → media, followed by Updater
activating the release. The Updater knows neither PostgreSQL nor Local media
implementation details; it only verifies and executes the registered hook.

Host creates the service account, loopback Kestrel configuration, systemd unit
and Catalog-owned Admin-gated Nginx snippet. Database installs local PostgreSQL
if required, creates the dedicated role/database and initial schema only if
empty, or validates the existing schema without migration. Media creates the
configured local storage root and systemd write-access drop-in without deleting
existing files. Every stage is safe to rerun and fails closed on conflicting
configuration; a partially completed attempt can be retried without resetting
the database or images.

**Normal initial install (one button):** Platform Bootstrap → Updater
`Prepare` → Updater `Install` (registered hook automatically runs Catalog
`install` first). The emergency Rollout page invokes the same steps. Once the
Catalog is installed, normal Update/Rollback skip the initial hook. The release
must contain provisioning assets: older `v0.1.0-dev.1` must not be used for
automated first installation.

For operators diagnosing a failed installation, the same independently
invocable operations remain available from the protected prepared artifact:

```bash
prepared_artifact=/opt/rustserver/plugin-catalog/prepared/<release-tag>
sudo bash "${prepared_artifact}/provisioning/provision.sh" host
sudoedit /etc/rustserver/plugin-catalog/plugin-catalog.env
sudo bash "${prepared_artifact}/provisioning/provision.sh" database
sudo bash "${prepared_artifact}/provisioning/provision.sh" media
```

Do not invoke these manually in the normal rollout. Changes to provider
configuration or media root must be reviewed before rerunning relevant stages.
MS SQL Server and S3 are unsupported and fail explicitly; there is no fallback.

## Пример media workflow

После создания Plugin доступный upload выполняется как multipart:

```powershell
curl.exe -X POST "http://localhost:5180/api/plugins/<pluginId>/media" `
  -F "file=@C:\images\plugin-icon.png" `
  -F "kind=Icon" `
  -F "caption=Primary icon" `
  -F "sortOrder=0"
```

Ответ включает stable `id`, `accessUrl` original и `thumbnailAccessUrl`. Назначить загруженную icon:

```powershell
@{ mediaId = '<mediaId>' } | ConvertTo-Json | Invoke-RestMethod `
  -Method Put `
  -Uri 'http://localhost:5180/api/plugins/<pluginId>/icon' `
  -ContentType 'application/json'
```

PowerShell 7 upload без curl:

```powershell
Invoke-RestMethod -Method Post `
  -Uri 'http://localhost:5180/api/plugins/<pluginId>/media' `
  -Form @{ file = Get-Item 'C:\images\plugin-icon.png'; kind = 'Icon'; caption = 'Primary icon'; sortOrder = '0' }
```

External video/preview — отдельный JSON workflow, без upload, proxy или server-side fetch:

```powershell
@{
  kind = 'Video'
  externalUrl = 'https://example.test/plugin-preview'
  caption = 'Preview video'
  sortOrder = 1
} | ConvertTo-Json | Invoke-RestMethod `
  -Method Post `
  -Uri 'http://localhost:5180/api/plugins/<pluginId>/media/external' `
  -ContentType 'application/json'
```

External media may only be `Video` or `Preview`; it has `accessUrl` equal to that external HTTP(S) URL, no thumbnail and no `/content` bytes endpoint.

## Доступные маршруты

| Route | Назначение |
| --- | --- |
| `GET /health` | Liveness без DB/storage IO. |
| `GET /health/ready` | Readiness через provider-neutral probes выбранных DB и media storage. |
| `GET /openapi/v1.json` | Development-only real OpenAPI. |
| `GET`, `POST /api/plugins` | List/search/filter/pagination и создание Plugin. |
| `GET`, `PUT`, `DELETE /api/plugins/{pluginId}` | Детали, metadata и удаление Plugin без media. |
| `GET`, `POST`, `PUT`, `DELETE /api/plugins/{pluginId}/sources` | Управление PluginSource. |
| `GET`, `POST /api/plugins/{pluginId}/media` | Ordered gallery и local multipart image upload. |
| `POST /api/plugins/{pluginId}/media/external` | External Video/Preview link. |
| `PUT`, `DELETE /api/plugins/{pluginId}/media/{mediaId}` | Metadata edit и delete media. |
| `PUT /api/plugins/{pluginId}/icon` | Select or clear existing Icon media. |
| `GET /api/plugins/{pluginId}/media/{mediaId}/content` | Original; use `?variant=thumbnail` for static WebP thumbnail. |

Full semantics, response DTOs, errors and limits are in [`docs/api-contract.md`](docs/api-contract.md). Architecture, storage safety and portable migration boundaries are documented in [`docs/architecture.md`](docs/architecture.md), [`docs/data-model.md`](docs/data-model.md) and [`docs/storage-and-migration.md`](docs/storage-and-migration.md).

## Security boundary

Authentication and authorization are intentionally not implemented inside this service. Catalog-owned host provisioning installs an Nginx location using the host's existing Admin `auth_request` for every Catalog route; the Kestrel listener remains loopback-only. The Catalog does not invent a separate login or authorization model.

S3, MS SQL, DataMigration, automatic version checks, Rust file scanning and plugin deployment remain out of scope. Schema migrations run only by the explicit operator command; neither API startup nor provisioning auto-migrates, drops, or recreates a populated database.
