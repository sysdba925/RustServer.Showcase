# RustServer.PluginCatalog — Stage 04 / 09: Catalog API

## Контекст

Работай **только** в репозитории `RustServer.PluginCatalog` и на текущей рабочей ветке. Продолжай результат этапов 1–3, не перепроектируй его заново. Текущий подтверждённый финальный commit этапа 3: `f9e03b013f606de2a27d5f74e5f7030a888fd897`, перед ним `0b9754e124e21a5a308d507d64fddb65f97d739e` и документационный коммит `f9c4810`. Сначала проверь `git status`, `git log` и изучи актуальные файлы; если локальный HEAD отличается, опирайся на актуальное состояние, не сбрасывая пользовательские изменения.

Прочитай полностью перед началом:

- `AGENTS.md`;
- `owner-view/PROJECT_PLAN.md`;
- `docs/architecture.md`;
- `docs/data-model.md`;
- `docs/api-contract.md`;
- `docs/storage-and-migration.md`;
- текущие реализации сущностей и EF Core mappings в `Features/PluginCatalog/Plugins/Domain` и `Infrastructure/Persistence/PostgreSql`;
- `README.md` и `Program.cs` / composition root.

**Цель:** реализовать этап 4 из согласованного плана: REST API ручного управления каталогом плагинов и источниками, с реальными command/query handlers и PostgreSQL persistence. Это каталог **известных** плагинов, а не установленных на Rust-сервере.

## Объём этапа (implement)

### API плагинов

- `GET /api/plugins` — список с поиском, фильтром `sourceKind`, нумерованной пагинацией, детерминированной сортировкой и общим числом совпадений.
- `POST /api/plugins` — создать запись: name, slug, description, необязательные currentKnownVersion / lastPublishedAt. Стабильный GUID генерирует backend. Иконка не обязательна.
- `GET /api/plugins/{pluginId:guid}` — детали плагина и источники. До этапа 5 media и icon отсутствуют/пусты в соответствии с документированным DTO; не подставлять фиктивные URL.
- `PUT /api/plugins/{pluginId:guid}` — редактировать метаданные, с понятной политикой изменения slug.
- `DELETE /api/plugins/{pluginId:guid}` — удалить плагин **только если у него нет записей media**. При наличии media вернуть документированный `409 Conflict` (очистка реальных файлов будет частью этапа 5); источники принадлежат плагину и могут удаляться каскадно. Обязательно учесть существующий составной FK `Plugin.IconMediaId -> PluginMedia`, не ослаблять ссылочную целостность.

### API источников

- `GET /api/plugins/{pluginId:guid}/sources` — список источников.
- `POST /api/plugins/{pluginId:guid}/sources` — добавить источник с типом `PluginSourceKind`, HTTP(S) URL и необязательной подписью.
- `PUT /api/plugins/{pluginId:guid}/sources/{sourceId:guid}` — редактировать источник, проверять его принадлежность указанному Plugin.
- `DELETE /api/plugins/{pluginId:guid}/sources/{sourceId:guid}` — удалить источник того же плагина.

Не добавляй API для медиа и иконок: `POST /media`, `PUT /icon`, выдача байтов и thumbnails — **этап 5**. Не добавляй installed/updateAvailable, скачивание обновлений, Git/release/deploy, Rust server scan, web UI или взаимодействие с другими сервисами.

## Архитектура — строго сохранить

**Feature-Based Architecture + Command Handlers + Orchestration + Ports & Adapters.**

- Используй корень `Features/PluginCatalog/`, внутри выделяй `Plugins` и `Sources` с `Commands`, `Queries`, transport endpoint/DTO и нужными ports по мере использования. Не устраивай плоский каталог `Services/` или «суперсервис» с гигантским CRUD-классом.
- HTTP endpoints только принимают/валидируют transport, вызывают handler и отображают результат в HTTP. Handler выполняет сценарий, не зависит от ASP.NET Core или EF Core.
- **Persistence port** принадлежит feature и выражает нужды use cases; **EF/Npgsql adapter** остаётся в `Infrastructure/Persistence/PostgreSql`. Не передавай `DbContext` в handlers и не выставляй `IQueryable` наружу. Фильтрацию/пагинацию выполнять в БД, не выкачивать весь каталог в память.
- Не создавай ненужные generic repositories, mediator, новые проекты или интерфейсы «на будущее». Минимальное число абстракций под реальные сценарии.
- Расширь доменные модели явными методами изменения состояния (`Plugin`, `PluginSource`), сохрани существующую валидацию, инкапсуляцию и совместимость с EF; не редактируй private setters через reflection и не дублируй доменные правила в endpoint.
- `Program.cs` — короткая композиция; регистрацию фичи/эндпоинтов держи в выделенных extension modules. `/health` и Development-only `/openapi/v1.json` сохраняются.
- Стабильные GUID, `StorageKey` и отделение metadata/storage должны оставаться переносимыми к MS SQL и S3 в будущем. Новый provider или `RustServer.DataMigration` сейчас **не реализуются**.

## Контракт и поведение (зафиксировать до кода)

Обнови `docs/api-contract.md` из черновика в контракт реализованных маршрутов этапа 4. Определи конкретные поля request/response, обязательность, значения enum и форматы дат. Реализуй это **синхронно с OpenAPI**, опубликованным через штатный `Microsoft.AspNetCore.OpenApi` (без нового Swagger UI по умолчанию).

Минимальные правила:

- `PluginSummaryDto`: `id`, `name`, `slug`, краткое представление описания, `currentKnownVersion`, `lastPublishedAt`, `updatedAt`, `icon` = null пока нет медиа.
- `PluginDetailsDto`: расширение summary с `description`, `createdAt`, `sources`, `iconMediaId` = null, `media` = пустой массив до этапа 5. Не публикуй `StorageKey`, абсолютные пути и connection string.
- `PluginSourceDto`: `id`, `kind`, `url`, `label`.
- `PagedResult<T>`: `items`, `page`, `pageSize`, `totalItems` (при необходимости `totalPages`). Предлагаемые значения: page = 1, pageSize = 20, максимум 100; установи однозначные ограничения и отрази их в документации.
- Детерминированная сортировка по имени и GUID для одинаковых имён; поиск без учёта регистра по имени/slug/описанию; `sourceKind` фильтрует плагины, у которых есть хотя бы один источник требуемого типа. Убедись, что итоговое число совпадений соответствует фильтрам и не дублируется из-за join.
- Create возвращает `201 Created` c `Location`, чтение/изменение — `200`, delete — `204`, missing/неверная принадлежность — `404`, некорректные поля / pagination — `400`, конфликт уникальности slug / удаление с media — `409`.
- Ошибки возвращать унифицированным `application/problem+json` c машиночитаемым `code`; не возвращать клиенту SQL-исключения. Конфликт уникального slug обрабатывай адресно (включая реальную гонку по unique index), не превращай все ошибки БД в `409`.
- `CurrentKnownVersion` — значение **каталога**, не установленной версии Rust-плагина. `LastPublishedAt` и `UpdatedAt` имеют разный смысл.
- Не вводи внешнюю авторизацию по предположениям: зафиксируй её статус/предполагаемую границу интеграции без выдачи API за production-secure. Не публикуй наружу непроверенные URL файлов или файловые операции.

## Проверки и тесты

Принцип из `AGENTS.md`: **минимальное количество тестов** — один репрезентативный позитивный API/persistence happy path, который действительно подтверждает создание записи, чтение и сохранение источника; не добавляй много negative/boundary/combinatorial tests или сложные fixtures. Существующие 2 smoke/model tests сохраняются.

Обязательные технические проверки:

1. `dotnet restore`, `dotnet build`, `dotnet test` — отчёт о фактических результатах.
2. `/health` продолжает возвращать 200 без connection string и без подключения к PostgreSQL.
3. В Development `/openapi/v1.json` содержит **все реализованные маршруты каталога**, методы, request/response schemas; без несуществующих media endpoints. `/openapi/v1.json` вне Development не должен открываться.
4. Проверка runtime работы CRUD **на реальном PostgreSQL**, если доступна отдельная временная dev-БД. Разрешено использовать имеющийся локальный/контейнерный PostgreSQL и существующий **явный** инструмент `RustServer.PluginCatalog.DevSchema` — **не устанавливай системные компоненты и не изменяй Platform/Bootstrap без отдельного задания**. Не сохраняй секреты в репозитории. Никаких `EnsureDeleted`, автоматического создания схемы в API или миграций при startup.
5. Если локального PostgreSQL по-прежнему нет, не называй persistence integration проверенным: покажи точный blocker и команды для будущей валидации; не маскируй отсутствие базы EF InMemory-тестом. Остальные проверки выполняй полностью.

## Документация и дисциплина

- Поддержи `docs/architecture.md`, `docs/api-contract.md`, `docs/data-model.md`, `owner-view/PROJECT_PLAN.md` и `README.md` в соответствии с фактическим состоянием этапа 4. В частности, исправь устаревший нижний раздел `PROJECT_PLAN.md`, который всё ещё говорит, что следующий шаг — этап 1.
- Не создавай EF migrations. Не редактируй `RustServer.Platform`, `RustServer.Web`, `RustServer.Plugins`, `RustServer.Versions`.
- Отделяй Stage 4 от Stage 5. Изменения схематического mapping только если необходимые для правильного поведения Stage 4, с пояснением и без разрушения данных.
- В конце: дай краткий список реализованных API, изменённых файлов, итоги build/test/OpenAPI/реального PostgreSQL, ограничения, SHA коммита и чистоту `git status`. Коммит только в текущем репозитории по принятому в проекте порядку. Никаких изменений production и деплоя.

## Критерий готовности

API позволяет вручную создавать/читать/обновлять/удалять каталожные **Plugin** и **PluginSource**, искать, фильтровать и листать каталог через PostgreSQL adapter; опубликованный OpenAPI соответствует реальным маршрутам. Никаких операций с игровым сервером, реальными медиафайлами, Web или bootstrap. Если PostgreSQL недоступен, явно оставь реальную интеграционную проверку непроверенной — это не отменяет необходимости выполнить весь доступный этап 4.
