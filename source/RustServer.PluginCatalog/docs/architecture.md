# RustServer.PluginCatalog — техническая архитектура

**Статус:** этап 8 сохраняет независимые Catalog-owned Host, Database и Media операции; первый Install вызывает их через проверенный `provision.sh install` hook. HTTP API выбирает PostgreSQL и Local Filesystem через configuration/DI; MS SQL Server и S3 пока намеренно не реализованы.
**Назначение:** независимый сервис каталога плагинов Rust: метаданные, известные версии, источники и медиа. Он не определяет, какие плагины установлены на конкретном игровом сервере.

Перед кодингом вместе с этим документом нужно прочитать [`../owner-view/PROJECT_PLAN.md`](../owner-view/PROJECT_PLAN.md) и [`../AGENTS.md`](../AGENTS.md).

## 1. Границы сервиса

| Компонент | Ответственность | Не является ответственностью PluginCatalog |
| --- | --- | --- |
| `RustServer.PluginCatalog` | Каталог, известные версии, описания, источники, метаданные и медиа | Файлы реальных плагинов, Git, Release, Deploy и жизненный цикл Rust |
| `RustServer.Plugins` | Git-управляемые файлы, Staging/Production, Release и Deploy | Универсальный каталог плагинов и галерея |
| `RustServer.Versions` | Версии Rust/Oxide и его собственная область проверки версий | Запись и хранение каталожных данных |
| `RustServer.Web` | Операторский интерфейс | Владение данными каталога |
| `RustServer.Platform` / Updater | Общий lifecycle release package и audited opt-in install hook | Выбор Catalog provider, роль/БД/schema, media root, systemd/Nginx шаблоны и provider-specific команды |

Сервис не делает автоматический обход сайтов, загрузку обновлений, сканирование игрового сервера или Deploy. Эти возможности не подразумеваются только потому, что в записи есть URL либо известная версия.

`RustServer.Plugins` по-прежнему не предоставляет API каталога. Catalog release не становится частью Plugins deployment, Git или runtime workflow. Даже если generic Platform/Updater позже устанавливает package, он не имеет знания о PostgreSQL/MS SQL Server или Local Filesystem/S3.

## 2. Выбранный стиль

Используется **Feature-Based Architecture + Command Handlers + Orchestration + Ports & Adapters** в одном ASP.NET Core приложении. Фича — целостная операторская возможность, а не один endpoint, контроллер или таблица.

- **Command** описывает изменяющее намерение; его handler владеет порядком шагов сценария.
- **Query** получает данные без изменений; не нужно искусственно оборачивать её в command.
- **Orchestration** находится в handler: он проверяет предметные условия, вызывает нужные ports и формирует результат. Это не отдельный workflow engine.
- **Port** выражает необходимую операции хранения или доступа к внешней системе. **Adapter** — конкретная реализация этого port.
- Домен не получает знания о PostgreSQL, пути к файлу, S3 URL или HTTP transport.

Не создавать глобальные слои `Domain`, `Application`, `Infrastructure`, `Api` как отдельные проекты и не вводить mediator-библиотеку без отдельного решения. Единый проект на первых этапах достаточно изолирует фичи и не добавляет искусственных границ.

## 3. Целевая структура

На этапе 5 feature получает команды, запросы, transport и persistence port, необходимые для Plugin, PluginSource и media. Media использует отдельный storage port; handler не знает local path, ImageMagick или конкретный provider.

```text
src/RustServer.PluginCatalog.Api/
  Program.cs
  CompositionRoot/
    DependencyInjection.cs
    EndpointRegistration.cs
  Features/
    PluginCatalog/
      PluginCatalogFeatureRegistration.cs
      PluginCatalogEndpoints.cs
      CatalogResult.cs
      Transport/
        CatalogHttpResults.cs
      Plugins/
        Commands/
        Queries/
        Domain/
          Plugin.cs
          PluginSource.cs
          PluginMedia.cs
          PluginMediaKind.cs
        Transport/
      Sources/
        Commands/
        Queries/
        Transport/
      Media/
        Commands/
        Queries/
        Transport/
      Ports/
        IPluginCatalogStore.cs
        IPluginMediaStorage.cs
  Infrastructure/
    Persistence/
      PostgreSql/
        PluginCatalogDbContext.cs
        *Configuration.cs
        PostgreSqlPluginCatalogStore.cs
        DependencyInjection.cs
    MediaStorage/
      Local/
        LocalPluginMediaStorage.cs
        DependencyInjection.cs
  Properties/
    launchSettings.json               # только локальный Development profile
  appsettings.json                    # стандартная ASP.NET Core конфигурация
provisioning/
  provision.sh                         # install (host → database → media) и независимые host | database | media
  host/                                # config, systemd и Nginx templates
  database/postgresql/                 # initial schema and validation
tests/RustServer.PluginCatalog.Api.Tests/
  HealthEndpointTests.cs
  PersistenceModelTests.cs
tools/RustServer.PluginCatalog.DevSchema/
  Program.cs                           # явный Development-only EnsureCreated
```

`PluginCatalog` — корень фичи. `Plugins/Domain` владеет связанными типами `Plugin`, `PluginSource` и `PluginMedia`; это не HTTP DTO и не EF-модели. `Plugins`, `Sources` и `Media` имеют собственные Commands, Queries и Transport. Общий `Ports/IPluginCatalogStore` выражает реальные потребности metadata-сценариев, не выдаёт `IQueryable` и реализован PostgreSQL adapter. `IPluginMediaStorage` выражает сохранение, чтение и удаление binary media и реализован Local Filesystem adapter.

`PluginCatalogFeatureRegistration` — feature entry point для handlers. `CompositionRoot/DependencyInjection.cs` подключает feature и provider resolver: он выбирает concrete adapter по `Database:Provider` и `MediaStorage:Provider`. `Program.cs` не импортирует PostgreSQL или Local Filesystem. `EndpointRegistration.cs` регистрирует инфраструктурные и feature endpoints. Это сохраняет `Program.cs` коротким и не делает CompositionRoot местом бизнес-логики.

`Infrastructure/Persistence/PostgreSql` содержит `PluginCatalogDbContext`, EF-конфигурации и DI-регистрацию Npgsql. Он не является местом для фичевых command handlers или HTTP DTO. `Infrastructure/MediaStorage/Local` не создан до этапа 5.

## 4. Поток сценариев

Общий путь выглядит так:

```text
HTTP endpoint
  → transport mapping и поверхностная валидация
  → Command/Query handler
  → domain checks и orchestration
  → Port
  → PostgreSQL / Local Filesystem adapter
  → DTO или контролируемая ошибка HTTP
```

Изменение описания или slug использует тот же фактический путь:

```text
PUT /api/plugins/{pluginId}
  → UpdatePlugin handler
  → загрузить Plugin через порт каталога
  → проверить и применить допустимые изменения доменной записи
  → сохранить через порт
  → вернуть Plugin DTO
```

Загрузка media выполняет следующий согласованный сценарий:

```text
POST /api/plugins/{pluginId}/media
  → UploadMedia handler
  → проверить существование Plugin и допустимость metadata
  → Media Storage port копирует upload во временный файл, проверяет magic bytes и декодируемость
  → Local adapter генерирует StorageKey и атомарно публикует original + thumbnail
  → Catalog persistence port сохраняет PluginMedia metadata
  → при ошибке DB handler пытается удалить новые файлы и логирует неудачный cleanup
  → вернуть Media DTO с URL доступа
```

Удаление сначала в одной EF Core `SaveChanges`-транзакции очищает `Plugin.IconMediaId` (если выбрана удаляемая иконка) и удаляет metadata; лишь после подтверждённого DB commit handler удаляет local original/thumbnail. Ошибка физического удаления оставляет допустимый orphan file, логируется и может быть устранена повторным безопасным удалением; она не возвращает ссылку DB на отсутствующие bytes. Автоматический orphan-cleanup job не является частью этапа 5.

## 5. Порты и adapters

Порты определяются там, где они нужны сценарию, а не заранее ради каждой сущности. `IPluginCatalogStore` необходим Commands/Queries Plugin, Source и Media: он выражает загрузку, фильтрацию/pagination в БД, проверку slug, сохранение, ownership media и контролируемое удаление Plugin. PostgreSQL adapter владеет `DbContext` и не передаёт его handler. `IPluginMediaStorage` принимает поток upload, возвращает подтверждённые content type/StorageKey, выдаёт original или thumbnail и безопасно удаляет оба файла.

| Нужда фичи | Port | Первый adapter | Будущие варианты |
| --- | --- | --- | --- |
| Чтение и запись каталожных метаданных | persistence port каталога | PostgreSQL | MS SQL Server, отдельный service adapter |
| Сохранение, чтение и удаление байтов медиа | media storage port | Local Filesystem | S3, отдельный Storage API |

Конкретные интерфейсы создаются только вместе с первым handler, который действительно использует эту границу. Не нужны интерфейсы для простых внутридоменных операций и не нужно заранее создавать MS SQL/S3 adapters.

## 6. HTTP и composition root

`Program.cs` будущего API содержит только создание `WebApplicationBuilder`, вызовы registration-модулей, middleware pipeline, mapping endpoints и `Run`. В нём запрещены inline endpoints, SQL, доступ к файлам и handler-логика.

Endpoint остаётся тонким: переводит HTTP request в команду/запрос, передаёт cancellation и отображает результат в черновой HTTP contract. Handler не должен зависеть от ASP.NET типов; adapter не должен возвращать HTTP response.

`WebApplication.CreateBuilder` использует стандартные providers `appsettings.json`, environment variables и command-line arguments. `Database:Provider` и `MediaStorage:Provider` обязательны: composition root выбирает registered adapter строго по этим значениям и сразу выдаёт понятную ошибку для отсутствующего или неподдерживаемого provider; fallback нет. PostgreSQL использует `ConnectionStrings:PluginCatalog`; Local Filesystem использует explicit `MediaStorage:RootPath` и limits. Root не создаётся и не проверяется до media operation. Поэтому `GET /health` не требует database или media I/O. Отдельный `GET /health/ready` зависит только от provider-neutral readiness ports: выбранный database adapter проверяет требуемые metadata, выбранный storage adapter — уже provisioned storage. Он не создаёт schema, root или Catalog data. `EndpointRegistration.cs` публикует health, Development-only `GET /openapi/v1.json` и feature routes. Swagger UI не подключён. Development OpenAPI включает `ProblemDetails.code` как string extension.

## 7. Эволюция без преждевременной реализации

Контрактные DTO отделены от моделей persistence и не содержат физический путь, connection string или provider-specific URL. Поэтому в будущем доменная часть может быть вынесена в отдельный сервис или заменён adapter без переписывания уже опубликованного HTTP контракта. Такое выделение не запланировано сейчас и не оправдывает дополнительные проекты или сетевые вызовы на этапе 4.

Для инфраструктуры обязательны постоянные каталоги вне release-артефактов и Catalog-owned explicit provisioning как единственный путь подготовки Catalog dependencies. Generic release lifecycle может установить готовый artifact, но не знает его providers. Production запускается из зафиксированных артефактов и не зависит от develop-ветки или исходного checkout.

## 8. Открытые технические решения

Следующие пункты ограничивают Stage 05 и не являются обещанием последующей реализации:

- Authentication/authorization не реализованы и не предполагаются по аналогии с соседними сервисами; до отдельной интеграции API не production-secure.
- На этапе 4 зафиксированы mutable slug с trim + lowercase invariant, адресный `slug_conflict`, page `1…100000`, pageSize `1…100`, DB-side filtering и детерминированная сортировка `name`, затем ID.
- Local Media Storage в первой версии принимает только PNG, JPEG, WebP и GIF. Adapter проверяет сигнатуру и полную декодируемость, ограничивает размер upload, общее число pixels и GIF frames, сохраняет original без преобразования и создаёт static WebP thumbnail из первого GIF frame. Magick.NET Q8 выбран потому, что поставляется как Apache-2.0 NuGet package с Windows/Linux-native binaries; используется только для обработки безопасно подтверждённых image formats.
- `StorageKey` всегда не содержит префикс `media/`: `{pluginId}/{mediaId}/original.{confirmed-extension}`. Root задаётся конфигурацией Local adapter (production соглашение `/var/lib/rustserver/plugin-catalog/media/`; Development default — игнорируемый `.local/plugin-catalog/media/`). Adapter запрещает absolute/traversal/backslash keys и reparse-point/symlink path components.
- Content URL строится только из PluginId и MediaId. UUID media делает URL immutable: original и thumbnail получают долгий `Cache-Control: public, max-age=31536000, immutable`; replacement original отсутствует, поэтому stale cache не возникает. Thumbnail доступен вариантом `?variant=thumbnail`; `accessUrl` всегда остаётся URL original.
- External media добавляется отдельным JSON endpoint только как HTTP(S) `Video` или `Preview`, без скачивания, проксирования или SSRF. Оно не имеет storage key/thumbnail/content endpoint.
- Release artifact Catalog содержит `provisioning/provision.sh` и templates. `host`, `database` и `media` — независимо запускаемые root-operated команды. Дополнительно `install` исполняет их по порядку только при первом Install через зарегистрированный, root-verified Updater Executor hook; команды никогда не запускаются при старте API, Update/Rollback или systemd restart и сами не запускают API. Updater `Prepare` verifies mutable staging, then its generic root Executor verifies the manifest again and copies the link-free artifact to the root-owned, non-group/world-writable `/opt/rustserver/<application>/prepared/<tag>` handoff. That handoff has no provider knowledge and does not provision or activate a release; Catalog commands reject all other locations, including staging. `host` создаёт service account, release/config roots, systemd unit и Admin-gated Nginx location; `database` сейчас поддерживает только local PostgreSQL peer role/database и initial schema, with root streaming protected SQL to unprivileged peer `psql` over stdin; `media` сейчас поддерживает только Local Filesystem root and owns a systemd drop-in that derives `ReadWritePaths` from the configured root. Existing roles, databases, schemas and files only undergo read-only validation on rerun; no drop/recreate/automatic migration occurs. Platform supplies only the root-owned `/etc/rustserver/nginx/applications.d/` server-context include point; Catalog `host` validates it and owns its own snippet there. Nginx пропускает `/plugin-catalog-api/*` только через существующий Admin `auth_request`, а Kestrel слушает только loopback.
- Стратегия сопоставления с реально установленными плагинами и межсервисный API для этого пока отсутствуют. `PluginId`, `Slug` и известная версия сами по себе не дают права считать запись установленной.

JSON import v2 использует транзакционную границу persistence adapter; preview не сохраняет данные, commit повторно проверяет совпадения и сохраняет коллекцию атомарно. Неизвестные справочники создаются только с явным `createMissing`.

ZIP export читает согласованный снимок метаданных через persistence port, затем полностью собирает временный архив до начала HTTP-ответа. Оригиналы читаются через media storage port; ошибка чтения отменяет экспорт. Временный файл удаляется при закрытии ответа.

Restore preview проверяет ограниченный ZIP v2 в памяти без извлечения путей и сравнивает его метаданные со снимком через тот же read-only persistence port. Он не вызывает операции записи БД или media storage.

Restore commit повторно использует валидатор ZIP. Отдельная операция persistence блокирует таблицы каталога и проверяет пустоту в транзакции, затем записывает метаданные и вызывает storage port для создания оригиналов и генерации thumbnail без перезаписи. При откате удаляются только созданные операцией файлы. Неопределённый результат commit и аварийное завершение не могут быть атомарно разрешены между БД и файловой системой без отдельного журнала; требуется операторская проверка.
