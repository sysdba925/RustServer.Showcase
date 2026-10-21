# Codex — RustServer.PluginCatalog / Stage 08 of 09
## Platform, Bootstrap, Updater deployment and production-safe configuration

**Точка входа:** `E:\Projects\rust\rust-server-platform`.
**Смежные репозитории:**
- `E:\Projects\rust\RustServer.PluginCatalog` — backend Stage 05, `06d1619f3a1f2105e5de3acc620ff203eafec2b1`;
- `E:\Projects\rust\RustServer.Web` — Web Stage 07, `d1ce587866c57ec345fe4a9c136ceffcd7b9521f`;
- `E:\Projects\rust\RustServer.Updater` — текущий исполнитель развёртывания и Emergency Rollout.

**ВАЖНО:** Это Stage 08, НЕ Stage 09. На каждом репозитории отдельный commit; не создавать публичный GitHub Release, не запускать настоящий VPS deployment и не менять действующие сервисы на чужом сервере без отдельного задания. Не коммитить секреты или незнакомые файлы. Убедиться, что рабочие деревья в ожидаемом состоянии до изменений.

## 1. Изучи существующую архитектуру, не реализуй старую схему развёртывания

Сначала внимательно изучи:
- `rust-server-platform/deploy/install.sh`, `deploy/bootstrap/bootstrap.sh`, `deploy/bootstrap/steps/*`, `deploy/bootstrap/provision-services.sh`, `deploy/config/applications.ini`, `deploy/config/rustserver-web.nginx.conf`, `deploy/systemd/*`, `deploy/bootstrap/tests/*`, `deploy/HOW_TO_INSTALL.md`, `config/topology.env.example`;
- отдельно **Development Bootstrap**: `scripts/bootstrap.sh`, `scripts/bootstrap.d/*`, `scripts/lib/topology.sh`, `scripts/dev/with-topology.sh`, `scripts/bootstrap/README.md`;
- в Updater: `README.md`, `docs/architecture.md`, `docs/publishing.md`, `src/RustServer.Updater.Api/Emergency/EmergencyPage.cs` и тесты, `ApplicationRegistrationIniParser`, `DeploymentExecutor`;
- в Catalog: `AGENTS.md`, `owner-view/PROJECT_PLAN.md`, `docs/architecture.md`, `docs/api-contract.md`, `docs/storage-and-migration.md`, `README.md`, actual EF Core model + DevSchema, health endpoints, `MediaStorage` config;
- в Web: `AGENTS.md`, `vite.config.ts`, `src/shared/config/environment.ts`, `.env.example`, `README.md`, `docs/publishing.md` и маршрут `/dashboard/plugins/catalog`;
- правила shared changelog и актуальные `rust-server-platform/docs/ui/**`.

**Реальная текущая архитектура production:** Bootstrap делает create-only host provisioning; `RustServer.Updater` через проверенные GitHub Releases (`Prepare → Install/Update/Rollback`) развёртывает бинарные приложения. Production `RustServer.Web` — StaticWeb через Nginx, не Vite/Node/source checkout. У API-путей `auth_request` через `RustServer.Auth` с Admin role. Не добавлять старый production flow с `git clone`, `dotnet build` на VPS, вручную созданными current links, самостоятельным deploy скриптом, запуском из development workspaces или отдельной системой обновления.

Проверить реальные имена и paths в актуальных checkout: перечисленные SHA — ориентиры для базовых этапов, но актуальный код важнее предположений. Если правила нынешнего окружения изменились, следовать фактическим контрактам и объяснить отличие.

## 2. Целевая production topology

Новый зарегистрированный сервис `RustServer.PluginCatalog`:
- `Type=DotNet`, репозиторий `RustServer.PluginCatalog`, install directory `/opt/rustserver/plugin-catalog`, releases + `current` управляются **только Updater**;
- entrypoint `RustServer.PluginCatalog.Api.dll`, systemd `rustserver-plugin-catalog.service`;
- внутренний HTTP listener — `127.0.0.1:5116` (проверь конфликт порта с текущей topology до фиксации), и внешний same-origin prefix **`/plugin-catalog-api`** с сохранением `/api/...` путей;
- persistent media root `/var/lib/rustserver/plugin-catalog/media` (вне любых `releases`, `current`, runtime Git workspaces, игровых snapshots);
- отдельная PostgreSQL database и минимально привилегированный сервисный identity без superuser/grant к чужим DB; не добавлять PostgreSQL зависимости в `RustServer.Plugins`;
- полноценный admin-only Nginx proxy через **существующий** Auth `auth_request /_rustserver/auth/authorize-admin`, как у остальных `*-api`; прямой публичный bind backend запрещён. Для multipart учесть реальный 10 MiB upload и HTTP overhead (Nginx body limit не должен по умолчанию отклонять картинки больше 1 MiB).

Не путай `/health` (только liveness) с реальной готовностью Catalog CRUD. Updater не должен считать развёртывание завершённым, если PostgreSQL/schema или media root недоступны. Реализуй/используй отдельный дешёвый `GET /health/ready`, если нужен, и зарегистрируй `HealthPath=/health/ready` после уточнения контракта. Readiness должен проверять фактическую способность чтения нужных таблиц и работы persistent media root (проверка доступности/прав записи); без долгих или разрушающих действий, выдачи секретов и автосоздания схемы. Не ломай прежний `/health` и Development OpenAPI. Потребуется маленькая правка в `RustServer.PluginCatalog`, документировать API.

## 3. PostgreSQL и schema initialization — без потери данных

**Это критический блок:** backend Stage 05 имеет `EnsureCreatedAsync` ТОЛЬКО в Development-only tool; реальная PostgreSQL-схема до сих пор не проверялась. Нельзя притворяться, что установка PostgreSQL автоматически создаёт три таблицы и делает Catalog работающим.

- Production Bootstrap устанавливает или проверяет локальный PostgreSQL (Ubuntu 24.04 x64), создаёт отдельные DB/role/доступ с minimum privilege, защищает connection parameters, не открывает TCP PostgreSQL публично, не переписывает существующие кластеры/пользователей.
- Продумай минимальный **явный, воспроизводимый и недеструктивный initial-schema** путь из зафиксированных/проверенных артефактов. Схема обязана соответствовать фактическому EF Core mapping `plugins`, `plugin_sources`, `plugin_media` (UUID, UTC, уникальный slug, media ownership, составной FK icon, check constraints). Допустим вариант versioned SQL внутри Platform Bootstrap, выведенный из Catalog EF schema и проверенный на соответствие; допускается другой столь же безопасный вариант **без** зависимости production от development checkout. Нельзя выполнять Production `EnsureCreated`, `EnsureDeleted`, автосхемы при API startup или менять чужую БД.
- Только выделенная новая/пустая DB может получать initial-schema; существующую схему и данные Bootstrap обязан сохранять. Rerun idempotent, несовместимую схему отклонять с понятным сообщением; **не** auto-drop, auto-recreate, auto-migrate и не выдавать dev migrations за полноценный production upgrade.
- Учти, что пакет приложения появляется через Updater ПОСЛЕ Bootstrap: preflight/initialization должны быть действительно выполнимы в существующем порядке установки. Не оставлять «запустить где-то когда-нибудь вручную» вместо задокументированного working flow.
- Продумай безопасную аутентификацию app→PostgreSQL. Для односерверной инсталляции локальный Unix socket + PostgreSQL peer role для отдельного OS service account возможен и позволяет не хранить пароль; если выбран password-based путь — root-managed create-only secret с безопасными правами/systemd credential. Пароль не выводить и не помещать в git, logs, process command-line, world-readable environment. У сервиса не должно быть прав на чужие БД.
- Настрой явные ConnectionStrings/MediaStorage settings через production конфигурацию, никаких `.local` и connection strings в релизном архиве.
- Зафиксируй ограничения: перенос PostgreSQL→MS SQL и Local→S3 **не** реализуется; после появления ценных данных эволюция schema требует отдельного управляемого процесса.

## 4. Production Bootstrap + Updater registry + systemd

Изменения делай в существующем Production Bootstrap pipeline:
- create-only service account `rustserver-plugin-catalog` (или согласованное единообразное имя), root-owned installation roots `/opt/rustserver/plugin-catalog` и `releases`, writable persistent media только для service account;
- reviewed systemd unit: `WorkingDirectory=/opt/rustserver/plugin-catalog/current`, Production environment, loopback ASP.NET listener, connection configuration, абсолютный `MediaStorage__RootPath`, .NET 10 runtime; `Restart=on-failure`, неповышенные привилегии, отсутствие доступа к Rust game data/Git/release writes. Не создавать `current` вместо Updater;
- синхронизировать `deploy/config/applications.ini` с действительным runtime registry `/etc/rustserver/updater/applications.ini` через **консервативное add-if-missing** обновление существующей установки. Внимание: текущий Step 55 при rerun сохраняет старый runtime registry; одной правки template НЕДОСТАТОЧНО. Миграция только требуемой секции, atomic write, backup, проверка конфликтов `Directory/Service/HealthPort/Name`, preserve unrelated custom applications, reject malformed registry и avoid rewriting if present. Не редактировать чужие настройки и токены;
- Updater должен обнаруживать Catalog и устанавливать его через штатный Prepare/Install/Update/Rollback. Не раздавать root права Catalog. Не менять в Updater executor полномочия, протокол или общий release-state формат;
- текущая Emergency Rollout page в `RustServer.Updater` имеет **явный rolloutOrder**. Проверь его и, если для complete initial Rollout нужно добавить Catalog, внеси минимальную отдельную правку с order **после dependencies, до Web**, сохрани Resume Rollout и поведение уже установленных сервисов. Это изменение — отдельный commit в `RustServer.Updater`, а не тихое ожидание, что регистрация автоматически добавит его в Rollout. Если Updater нельзя безопасно менять в рамках данного шага, сделай явный documented manual Prepare→Install шаг и сообщи ограничение как blocker, а не заявляй Rollout полностью готовым;
- publishing/authorization чтения приватного репозитория — только существующий updater token по его правилам, без новых токенов или выдачи Catalog прав на GitHub.

## 5. Production Nginx

В `deploy/config/rustserver-web.nginx.conf`:
- `location = /plugin-catalog-api` → 404;
- `location ^~ /plugin-catalog-api/` → `auth_request /_rustserver/auth/authorize-admin`, `proxy_pass http://127.0.0.1:5116/` (URI-prefix stripping аналогично текущим APIs), Host/Forwarded headers по существующей конвенции;
- корректный upload body limit, чтобы 10 MiB multipart проходил (backend продолжает проверять свой лимит). Не отключай Admin gating для GET thumbnails, media, list или POST;
- не открывать backend port externally; не менять `/dashboard/`, `/plugins-api/` и static release-readiness logic;
- safe `nginx -t` перед reload и restore прежней конфигурации при ошибке в существующем Bootstrap.

Прямой доступ к `http://127.0.0.1:5116` — только на машине. Публичный `/plugin-catalog-api/api/...` без Admin session должен быть запрещён, авторизованный администратор должен обращаться к тому же пути; не создавать login внутри PluginCatalog только ради этой задачи.

## 6. Development integration (отдельно от production)

Development Bootstrap запускается из workspace; он может работать с `develop`, но **production не должен** зависеть от этого workflow.
- Добавь безопасный optional/required topology key для PluginCatalog URL, при необходимости bind host; обнови **все** перечисления/validator и безопасную миграцию уже существующего `/etc/rustserver/topology.env` без дубликатов;
- новый numbered sync/build/install unit workflow по стилю текущих `scripts/bootstrap.d/*` с реальным source checkout в Development, фиксированной dev службой и штатной `.NET 10` конфигурацией;
- обеспечь Development PostgreSQL schema и persistent media root без удаления пользовательских данных. Не писать dev connection string в git, не хранить пароль в `.env.example`;
- добавить Vite dev proxy в `RustServer.Web/vite.config.ts` для **`/plugin-catalog-api → RUSTSERVER_PLUGIN_CATALOG_URL`** по существующему `requireTopologyUrl`/`proxy` паттерну; сохранить pinned Production `/plugin-catalog-api`. Не встраивать адрес VM в build artifacts, не подставлять localhost fallback;
- сделать описания в `RUN.md`, README, dev topology и `RustServer.Web/README.md` консистентными. Production Nginx/Updater отличается от dev systemd/Vite.

## 7. Catalog GitHub Release publication (без фактической публикации)

`RustServer.PluginCatalog` сейчас имеет ASP.NET API, но нет production-ready GitHub Release pipeline. Добавь publish инфраструктуру по действующему проектному стандарту (ориентир `RustServer.Snapshots/scripts/publish.ps1`, `docs/publishing.md`, аналогичные `.pubxml`):
- framework-dependent `linux-x64` `net10.0` publish с DLL + deps + runtimeconfig + Magick.NET native binaries и остальными нужными зависимостями;
- artifact `RustServer.PluginCatalog-<vX.Y.Z[-dev.N]>-linux-x64.tar.gz` и `.sha256` по соглашению Updater;
- separate Development prerelease и Production stable channels; no mutable develop dependency at Production runtime, only verified tag+archive;
- проверка clean repo, origin, up-to-date head, gh auth, excluded secrets/local files, archive completeness и deterministic naming по sibling standards; поддержать dry-run;
- README `docs/publishing.md` с конкретными командами и требованиями;
- **не вызывать `gh release create` в этой задаче**; только dry-run/локальная упаковка без сетевого publishing. Не публиковать ни prerelease, ни stable, пока пользователь не попросил.

У Updater не должно появиться отдельной реализации download/deploy Catalog: его общий контракт должен принимать стандартный package.

## 8. UI docs, contract gap, changelog

Обновить только актуальные, реально затронутые Platform UI mapping docs, чтобы Каталог больше не числился чисто «будущей» возможностью: сервис независим от `RustServer.Plugins`, видимые `/dashboard/plugins/catalog` и `/:pluginId`, никакой install/deploy semantics. Не переписывать старую историю и не создавать новый экран-спецификацию без надобности.

Зафиксировать уже известный контрактный gap: Development OpenAPI `ProblemDetails` не отражает backend `code`. Если небольшой scoped fix в Catalog не нарушает API и легко проверяем — добавь OpenAPI schema transformer и минимальную проверку, не меняя контракт Web обходным способом. Иначе оставь честный явный blocker/next-step, не заявляй исправленным.

Добавь новый shared changelog `docs/changelog/iteration-NNN.md` с ближайшим свободным номером (после существующего 109, но перепроверь), описывая только реально сделанное. Синхронизировать `owner-view/PROJECT_PLAN.md` Catalog по результату, не отмечать Stage 09 выполненным.

## 9. Минимальные проверки и критерии приёмки

Проверяй то, что действительно изменил, а не наращивай сотни тестов:
- `dotnet restore/build/test` для Catalog (не писать большую матрицу тестов; 1 небольшой smoke для новой readiness/contract behavior максимум);
- `npm run lint`, `npm run typecheck`, `npm run test`, `npm run build` для Web;
- `bash -n`/ShellCheck (если доступен), текущие targeted Bootstrap/registry tests, `nginx -t` там, где доступен, контролируемая проверка Updater регистрации/rollout;
- dry-run artifact publish и содержимое tar.gz/sha256 — без GitHub write;
- если доступен disposable PostgreSQL, проверить **реально** создание схемы, CRUD и чтение/запись media. Не применять schema к рабочей/непустой DB, не пробовать продовую установку без согласованного test host;
- отдельно проверить на тестовом стенде, что proxy требует Admin, Catalog backend слушает loopback, release state/persistent DB/media не затрагиваются повторным Bootstrap/Updater rollback.

**Acceptance:** сервис полностью подготавливается через Bootstrap, появляется в Updater registry, может быть установлен из корректного GitHub Release через существующий Updater, Nginx предоставляет защищённый same-origin API, Vite умеет dev proxy, data directories не зависят от release. Должен существовать реальный путь применения initial PostgreSQL schema **до** проверки полноценной готовности Catalog. Не выдавать неисполненный clean-VM E2E (это Stage 09) за passed.

Если не хватает конкретной release, токена или VM — реализуй доступные изменения, отметь проверку как NOT RUN и объясни следующий явный шаг. Не придумывай успешные операции.

## 10. Финальный отчёт и commits

Отчёт нужен короткий, но конкретный:
- commits **отдельно**: Platform (включая changelog/UI docs), Catalog backend/publishing, Web dev proxy и Updater (только если изменён);
- какие пути installer/bootstrap/provisioning и systemd затронуты;
- выбранная DB auth, первичная schema init и её идемпотентность;
- как проходит Dev proxy и Prod Admin-protected Nginx path;
- реальный статус tests, packaging dry-run, PostgreSQL smoke, VM deployment — не смешивать;
- известные gaps/blockers, включая production installer bootstrap source pinning: проверь `deploy/install.sh`, где ранее использовался `REPOSITORY_BRANCH=develop`. Production channel не должен получать **новую** зависимость от mutable development ref. Если текущая цепочка продолжает брать Bootstrap из `develop`, отметь это как **существующий deployment invariant violation**, не скрывай и не заявляй full production independence без устранения. Согласованное исправление возможно в этом этапе, если оно не ломает installer trust/recovery и покрыто проверками; иначе честно вынеси как blocking issue до production;
- состояние каждого рабочего дерева, список оставшихся действий для Stage 09.

**НЕ начинай Stage 09 и не публикуй реальные релизы автоматически.**
