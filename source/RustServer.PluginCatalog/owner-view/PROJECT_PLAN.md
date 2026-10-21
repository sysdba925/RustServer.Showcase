# RustServer.PluginCatalog — Owner View / Project Plan

> Этот документ написан для владельца проекта. Это согласованный план, по которому сверяются этапы реализации. Технические ограничения находятся в `AGENTS.md`, детали решений — в `docs/`.

## Цель

Создать независимый микросервис **`RustServer.PluginCatalog`** и одноимённый репозиторий.

Каталог — это библиотека плагинов Rust, **не привязанная к факту установки плагина на конкретном сервере**. В первой версии поддерживаем ручное создание и редактирование плагинов, версии, источники, описание и медиа. В перспективе появятся карточки с галереями, GIF и видео, внутриигровые preview, а также совместное отображение с установленными плагинами. Позже каталог сможет быть источником данных для просмотра установленных плагинов, фильтрации и поиска обновлений.

### Разделение ответственности

| Компонент | За что отвечает |
|---|---|
| `RustServer.PluginCatalog` | Каталог плагинов, известные версии, источники, описания, иконки, скриншоты, GIF, галереи |
| `RustServer.Plugins` | Git, реальные файлы плагинов, Staging/Production, Release, Deploy |
| `RustServer.Versions` | Версии серверного программного обеспечения |
| `RustServer.Web` | Web UI каталога; позже — объединённые экраны по данным нескольких сервисов |
| `RustServer.Platform` | Общий release lifecycle; не знает Catalog providers, schema, media root или provisioning |

Никаких зависимостей production от development. `RustServer.Plugins` не должен получать зависимости на PostgreSQL, EF Core или галереи каталога.

## План: 9 этапов (9 последовательных промптов для Codex)

Не делать один большой промпт: каждый этап должен давать конкретный проверяемый результат.

| № | Этап | Объём | Содержание |
|---|---|---|---|
| 1 | **Архитектура** | Средний | Изучение Platform, существующих `AGENTS.md`, согласование моделей, API, Ports & Adapters, документация |
| 2 | **Основа сервиса** | Малый | ASP.NET Core, DI, конфигурация, OpenAPI, health checks |
| 3 | **Database** | Средний | PostgreSQL, EF Core, сущности Plugin, PluginSource, PluginMedia, репозитории |
| 4 | **Catalog API** | Большой | CRUD плагинов, источников, списки, поиск, фильтры, пагинация |
| 5 | **Media Storage** | Большой | Загрузка изображений/GIF, иконки, thumbnails, локальное хранилище, выдача через API |
| 6 | **Web: каталог** | Большой | Список, поиск, фильтры, иконки, создание и редактирование плагинов |
| 7 | **Web: карточка плагина** | Средний | Описание, источники, галерея, загрузка и управление медиа |
| 8 | **Catalog provisioning** | Средний | Отдельные Host, Database и Media операции из release artifact Catalog |
| 9 | **Интеграция и тестирование** | Средний | Чистая VM, API smoke tests, медиа, перезапуски, сохранность данных |

### Объём по направлениям

- Backend и архитектура — **5 этапов** (1–5).
- Web — **2 этапа** (6–7).
- Развёртывание и интеграция — **2 этапа** (8–9).

### Текущий статус реализации

- ✅ **Этап 1 — Архитектура:** завершён. Созданы owner-view, правила для агентов и техническая документация.
- ✅ **Этап 2 — Основа сервиса:** завершён. Есть `net10.0` ASP.NET Core API-каркас, стандартная конфигурация, liveness `GET /health`, OpenAPI JSON в Development, solution, README и один smoke test. Каталог плагинов, БД и медиа-хранилище ещё не реализованы.
- ✅ **Этап 3 — Database:** завершён. Добавлены переносимые доменные типы `Plugin`, `PluginSource` и `PluginMedia`, PostgreSQL EF Core/Npgsql mapping, безопасный явный Development-only инициализатор пустой схемы и один persistence model test. HTTP CRUD, файловое media storage и миграции между providers не реализованы.
- ✅ **Этап 4 — Catalog API:** завершён. Реализованы REST CRUD Plugin и PluginSource, database-side список/поиск/sourceKind-filter/pagination, Development OpenAPI, Problem Details и минимальный API happy-path test. Media operations, storage и Web не реализованы.
- ✅ **Этап 5 — Media Storage:** завершён. Реализованы Local Filesystem adapter, безопасная multipart-загрузка PNG/JPEG/WebP/GIF с проверкой формата и лимитами, original/thumbnail, ordered gallery, icon selection, external Video/Preview links, content API, compensation files/metadata и один реальный filesystem API smoke test. PostgreSQL integration smoke остаётся непроверенным без выделенной connection string.
- ✅ **Этапы 6–7 — Web:** завершены в RustServer.Web: независимые list/details/catalog metadata, Source и media workflows без install/deploy semantics.
- ✅ **Этап 8 — Catalog provisioning:** завершён с архитектурной коррекцией: Catalog поставляет отдельные явно запускаемые Host, Database и Media provisioning-инструменты, шаблоны systemd/Nginx/config и PostgreSQL schema SQL в собственном verified release artifact. Platform и Updater не знают выбранные Database/Media provider; generic Updater только вызывает verified Catalog-owned first-install hook.
- ⏭️ **Следующий отдельный этап:** 9 — чистая VM и сквозная интеграция. Не начинать его без отдельного задания.

## Обязательная архитектура

**Feature-Based Architecture + Command Handlers + Orchestration + Ports & Adapters.**

Понятное дерево функциональных корней, никаких плоских каталогов с десятками несвязанных файлов. `Program.cs` остаётся компактным composition entry point: регистрация/запуск, не бизнес-логика и не inline endpoints.

### Хранилища и расширяемость

| Порт | Реализация сейчас | Возможная реализация позже |
|---|---|---|
| Database Port | **PostgreSQL** | **MS SQL Server** или отдельный API/service adapter |
| Media Storage Port | **Local Filesystem** | **S3** или отдельный Storage API/service adapter |

PostgreSQL и локальный storage **не выносим** в дополнительные микросервисы сейчас. Не реализуем MS SQL/S3 заранее, но бизнес-логика не должна зависеть от конкретного провайдера.

### Сущности каталога

- `Plugin`: стабильный идентификатор, имя, slug, описание, известная версия, дата публикации/обновления автором (если известна), даты создания/изменения записи каталога.
- `PluginSource` (1:N): ссылки на сайт, GitHub, Codefling/uMod, документацию, потенциальный источник проверки версии.
- `PluginMedia` (1:N): иконки, скриншоты, GIF, видео/ссылки на видео, превью, подписи, сортировка, тип, StorageKey.
- `Plugin.IconMediaId` — **прямая ссылка на главную иконку**, чтобы получать её вместе с карточкой плагина без поиска по галерее. Допускается nullable до загрузки иконки.

`LastPublishedDate` у автора и `UpdatedAt` записи нашего каталога — разные данные. Возможны будущие категории, теги, авторы, история версий и `PluginInstallations`, но **не реализовывать** их без отдельного этапа.

Установка на Rust-сервере не является свойством базовой записи каталога. Установленность/текущую версию на сервере позже определяем в другой предметной области и связываем по согласованному идентификатору.

### Данные и файловая система

Медиа хранится **как файлы, не BLOB в БД**; в PostgreSQL — метаданные, `StorageKey` (относительный логический ключ), ссылки, порядок и подписи. Не сохраняем абсолютные пути или физические S3 URL как часть бизнес-модели.

Пример постоянного расположения вне каталога релизов:

```text
/var/lib/rustserver/plugin-catalog/media/
  {pluginId}/
    {mediaId}/
      original.png
      thumbnail.webp
```

Файлы, PostgreSQL и конфигурацию резервируем отдельно от релизов приложения и игровых snapshots.

### Portable Data Migration (будущая реализация)

Требуется в будущем перенос **PostgreSQL → PostgreSQL, PostgreSQL → MS SQL Server, Local → S3, S3 → Local/S3**. Не ограничиваться `pg_dump`, поскольку перенос должен быть независим от провайдера.

Предусмотреть собственный инструмент `RustServer.DataMigration` с операциями:

`Export → Import → Validate → Switch → Rollback`.

Портативный формат, например, содержит `manifest.json` с версией и хешами, `plugins.json`, `sources.json`, `media.json`, и каталог файлов `media/`. Требуются стабильные `PluginId`, `MediaId`, `StorageKey`, проверка полноты и контрольных сумм, переключение только после проверки и сохранение прежних данных для отката.

**На текущих 9 этапах инструмент не реализуем**, лишь проектируем интерфейсы и модель, совместимую с переносом.

## Принципы выполнения работ

- У каждого этапа — ограниченная зона изменений и критерии готовности.
- Чистая структура и простой `Program.cs`; бизнес-правила живут в feature-дереве.
- Минимальные тесты: **не превращать тестирование в половину разработки**. Один репрезентативный позитивный сценарий/smoke test для основной функциональности этапа; без большой матрицы негативных кейсов, если об этом не попросили отдельно. В документационном этапе 1 тесты не нужны.
- Согласованная архитектура, `AGENTS.md`, документация и changelog обязательны там, где применимо.
- Catalog infrastructure changes живут в этом репозитории и поставляются вместе с Catalog release artifact. Первая установка автоматически вызывает Catalog-owned `install` через generic verified Updater hook; независимые `host/database/media` команды остаются для ручного восстановления. Platform/Updater не получают Catalog-specific provider/configuration knowledge.
- Development rollout uses a clean PostgreSQL database and the current initial schema; automatic migration is not performed.
- Никакой production зависимости от development; развертывание должно работать по зафиксированным артефактам.

## Что сознательно не делаем сейчас

- Автоматическая проверка внешних версий и скачивание обновлений.
- Определение реально установленных плагинов и их версий на Rust-сервере.
- Инсталляция/деплой плагинов через каталог.
- MS SQL, S3, самостоятельный storage/database микросервис.
- Реализация `RustServer.DataMigration`.
- Сложная тестовая инфраструктура и расширенное покрытие.

## Следующий шаг

**Этап 9 — Integration and testing.** Включение Catalog в одношаговый Rollout реализуется через generic root-owned Updater first-install hook; реальные проверки на чистой VM обязательны до принятия этапа. Чистая VM, реальный PostgreSQL/media/API smoke, Admin gateway и Updater Install/Rollback проверяются только отдельным заданием.
