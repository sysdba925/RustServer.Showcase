# RustServer.PluginCatalog — правила для агентов

Перед любым кодингом прочитайте полностью:

1. [`owner-view/PROJECT_PLAN.md`](owner-view/PROJECT_PLAN.md) — цель, девять этапов и границы работ;
2. [`docs/architecture.md`](docs/architecture.md) — технические правила;
3. для затронутой области также `docs/data-model.md`, `docs/api-contract.md` и `docs/storage-and-migration.md`.

Документация и текущий этап определяют объём работы. Сначала обновляйте согласованное решение в документации, затем код. Не реализуйте возможности следующего этапа без отдельного задания.

## Архитектура

- Используйте **Feature-Based Architecture + Command Handlers + Orchestration + Ports & Adapters**.
- У каждой функциональной области есть собственный корень. Внутри неё размещаются `Commands`, `Queries`, `Domain` и нужные `Ports`; технологические адаптеры находятся в `Infrastructure`. Не создавайте плоский `Services/` с несвязанными классами.
- `Program.cs` — минимальная точка композиции: создать builder, вызвать DI-модули, настроить pipeline, зарегистрировать endpoints и запустить приложение. В нём запрещены inline routes, handler-, SQL- и файловая логика.
- Endpoints/Controllers остаются тонкими: транспортное преобразование → handler. Handler оркестрирует сценарий; ports выражают требуемый IO; adapters реализуют доступ к PostgreSQL, Local Filesystem и внешним системам.
- Не вводите интерфейсы, общие слои и папки «на будущее» без реальной границы или сценария. Не превращайте архитектуру в глобальное разделение `Domain/Application/Infrastructure/Api`.

## Данные и инфраструктура

- PostgreSQL и Local Filesystem — первые адаптеры. MS SQL Server, S3 и удалённые database/storage-сервисы — будущие варианты; доменная логика не должна зависеть от конкретного provider.
- Идентификаторы и `StorageKey` должны быть переносимыми. Бинарные медиа не хранятся как BLOB в БД; абсолютные пути и физические S3 URL не являются доменными данными.
- `RustServer.PluginCatalog` полностью владеет provisioning своих зависимостей. Его release artifact обязан содержать отдельно запускаемые Host, Database и Media provisioning-инструменты и их шаблоны. `RustServer.Platform` и Updater могут работать только с общим release lifecycle и не должны знать выбранные Catalog Database/Media provider, connection string, schema или media root.
- Host, Database и Media provisioning остаются независимо запускаемыми и идемпотентными операциями. Только явно зарегистрированный и проверенный generic first-install hook Updater может вызвать Catalog-owned `provision.sh install`, который выполняет все три шага по порядку перед первой активацией. Ни API startup, ни Update/Rollback, ни restart не запускают provisioning. Выбор provider идёт через конфигурацию и DI; неподдерживаемый provider выдаёт ошибку без fallback.
- Root provisioning запускается только из проверенной root-owned копии prepared artifact, а не из изменяемого Updater staging. При доступе непривилегированного adapter/process к release SQL или шаблону root-процесс читает защищённый файл сам и передаёт только нужные bytes через stdin/явный ограниченный канал. Systemd sandbox должен получать writable filesystem paths из Catalog-owned configuration/provider operation, а не из зашитого пути первого Local adapter.
- Provisioning существующих ролей, БД, схем и media files должен быть идемпотентным и консервативным: не удалять, не пересоздавать и не автоматически мигрировать существующие данные. Не меняйте `RustServer.Platform`, Updater, `RustServer.Web`, `RustServer.Plugins` или `RustServer.Versions` без отдельного задания.

## Контракт, тесты и дисциплина

- `docs/api-contract.md` на этапе 1 — черновик, а не опубликованный OpenAPI-контракт. При реализации HTTP API сначала согласуйте контракт, затем синхронно обновляйте реализацию, OpenAPI и документацию в соответствующем этапе.
- Для каждой реализованной функциональности добавляйте только небольшой репрезентативный happy-path/smoke тест. Не создавайте матрицы negative/boundary/combinatorial тестов, сложные фикстуры, snapshots или обещания 100% coverage без прямого запроса. Для документационных изменений тесты не нужны.
- Фиксируйте существенные продуктовые, архитектурные и контрактные решения в changelog, когда в репозитории определён порядок его ведения. Не создавайте changelog-шум для форматирования и внутренних рефакторингов.
- Не меняйте `RustServer.Web`, `RustServer.Plugins`, `RustServer.Versions` или `RustServer.Platform` в рамках задач этого репозитория, если отдельный этап явно не расширил scope.

Историческое ограничение этапа 1 больше не применяется к уже реализованным последующим этапам. Новые изменения по-прежнему должны оставаться в согласованном scope текущего задания.
