# Codex — RustServer.PluginCatalog / Stage 09 of 09
## Clean-VM Integration, Deployment Verification & Final Acceptance

**Рабочая точка входа:** `E:\Projects\rust\rust-server-platform`.

**Смежные репозитории и Stage 08 коммиты:**
- `rust-server-platform`: `0982ee4` — Bootstrap, PostgreSQL, schema, media root, production registry, systemd, Nginx и dev topology;
- `RustServer.PluginCatalog`: `75ad482` — readiness `/health/ready`, Linux release packaging;
- `RustServer.Web`: `e851365` — development Vite proxy;
- `RustServer.Updater`: локальный SHA `affec03` по отчёту — **сначала подтвердить, что коммит существует на origin; при предыдущей проверке через GitHub он не находился**. В нём должен быть Catalog в Emergency rollout order.

**Цель этапа:** на выделенном тестовом Ubuntu 24.04 x64 стенде подтвердить работающую цепочку: Bootstrap provisioning → PostgreSQL/schema/media root → проверенный пакет Updater → Catalog readiness → Nginx Admin boundary → Web Catalog CRUD/media → рестарт/rerun/rollback без потери данных. НЕ подменять интеграцию повторным unit-тестированием. Если необходимых условий для реального E2E нет, отчёт должен ясно говорить `BLOCKED/NOT RUN`, а не `PASSED`.

## 1. Безопасность до первых действий

1. Прочитай `AGENTS.md`, `owner-view/PROJECT_PLAN.md`, `docs/*` PluginCatalog, `deploy/HOW_TO_INSTALL.md`, `deploy/bootstrap/README.md`, `deploy/bootstrap/provision-services.sh`, `deploy/bootstrap/steps/*`, `deploy/config/applications.ini`, `deploy/config/rustserver-web.nginx.conf`, `deploy/plugin-catalog/*.sql`, `deploy/systemd/rustserver-plugin-catalog.service`, `scripts/bootstrap.d/*`, `RustServer.Updater/README.md`, `docs/architecture.md`, `src/RustServer.Updater.Api/Emergency/EmergencyPage.cs`, правила shared changelog и Web Catalog API.
2. Сделай **preflight** `git status`, `git log`, `git remote -v`, наличие всех Stage 08 коммитов в origin (отдельно `affec03`), актуальность рабочих деревьев. Не объединять незакоммиченные изменения с текущими. Если Updater commit отсутствует на origin, **не считать Emergency Rollout готовым**. Не выполнять `git push` без явной команды владельца; сообщить команду/действие для владельца.
3. Тестовый стенд — только заранее выделенная одноразовая VM `rust-clean` (если фактически существует и владелец разрешил её использовать) или эквивалентный явно выделенный disposable Ubuntu. `rust-dev`, рабочий VPS, игровые данные, production БД и production releases не трогать. Не создавать/удалять VM, не запускать `cleanup.sh` или другие разрушительные команды, если такое действие явно не разрешено. Не затрагивать чужие контейнеры/данные.
4. Не публикуй GitHub Release и не меняй токены/пароли/авторизацию без явного разрешения владельца. Секреты не выводить в логи, отчёт, скрипты, stdout и Git. Не копировать session cookies или PAT в test artifacts. До фактической публикации нового Release допустимы read-only inspection и dry-run. Если для E2E необходима публикация — остановись на этом gate, покажи готовый artifact/tag и нужное действие владельца; НЕ утверждай, что E2E завершён.
5. Различай Development Bootstrap из исходников и **Production Installer + Updater**. Положительный dev smoke не означает production acceptance. Не менять release pointers/current вручную и не обходить Updater.

## 2. Быстрый повторный контроль кода и известных пробелов

- Проверь фактический `rolloutOrder` в **доступной удалённой версии Updater**: `RustServer.PluginCatalog` должен находиться до `RustServer.Web`; отдельно указать, запушен ли `affec03`.
- Проверь `deploy/install.sh`: `REPOSITORY_BRANCH="develop"` остаётся известным supply-chain/reproducibility gap. Runtime использует verified GitHub Releases, но сам Installer загружает **изменяемый Bootstrap source**. Зафиксируй как production-signoff blocker до одобренного решения: immutable commit/tag с проверяемым trust chain или другой согласованный способ. Не объявляй независимость всего production installer от `develop`. Не делай необоснованную однострочную замену на SHA, которая затруднит выпуск исправлений.
- Проверь фактическую доступность/контракт `ProblemDetails.code` в Development OpenAPI. Отсутствие extension (если не исправлено) — отдельный contract gap; не меняй Web типы по догадке.
- Сверь PostgreSQL `initial-schema-v1.sql` с EF Core mapping и `verify-schema-v1.sql`. **Проверка реальным PostgreSQL обязательна**, потому что строки `pg_get_constraintdef`, типы, названия индексов и grant/peer authentication ранее не проверялись в живой СУБД.
- Перепроверь Nginx `client_max_body_size` >= 10 MiB + multipart overhead, Admin `auth_request`, strip-prefix, HTTP binding на 127.0.0.1. Имена путей брать из текущих файлов, не из памяти.

## 3. Preflight окружения и артефактов (без мутаций)

Отдельно запиши факт/результат:

- Ubuntu 24.04 x64; `systemctl`, `psql`, `nginx`, ASP.NET Core runtime 10, свободен/занят TCP 5116; приложение Rust не требуется для Catalog smoke.
- Имеется ли разрешённый тестовый target, GitHub доступ и подходящие **Development** releases `RustServer.PluginCatalog`, `RustServer.Web`, `RustServer.Updater` в том же канале, что использует установленный Updater; их packaging, `.sha256` и tag. Не создавать релизы автоматически.
- `dotnet publish` Catalog в `Release linux-x64` и publisher dry-run из синхронизированного `develop`; tar.gz должен содержать DLL, deps/runtimeconfig и нужные native Magick.NET binaries. При отсутствии `gh`/токена честно `NOT RUN` с причиной.
- Полный Windows-dependent Updater suite дал 111 passed / 2 failed в Stage 08: проверить эти **две конкретные проверки на Ubuntu**, если безопасно и доступно, не расширять тестовую матрицу. Приоритет — проверка runtime поведения.

## 4. Production Bootstrap на безопасной чистой VM

Если среда и разрешения действительно позволяют, выполнить штатную установку строго по `rust-server-platform/deploy/HOW_TO_INSTALL.md` и `deploy/bootstrap/README.md`, **не изобрести новый install flow**. Применять только сохранённые/одобренные credentials; запросить ручной ввод через официальную процедуру, не раскрывая значения.

Ожидаемые состояния **после Bootstrap, но до Updater Install**:
- PostgreSQL установлен и активен, не слушает публичный интерфейс;
- создан отдельный Unix peer role/database `rustserver-plugin-catalog` / `rustserver_plugin_catalog`, без привилегий к другим DB;
- SQL v1 таблицы `plugins`, `plugin_sources`, `plugin_media` и все ожидаемые FK, check/indexes реально существуют; `verify-schema-v1.sql` проходит;
- каталог `/var/lib/rustserver/plugin-catalog/media` существует, принадлежит `rustserver-plugin-catalog`, сохраняется вне `/opt/rustserver/plugin-catalog/releases`;
- reviewed `rustserver-plugin-catalog.service` создан, **но ещё не запущен и не активирован Bootstrap**; `current` управляется Updater;
- runtime registry `/etc/rustserver/updater/applications.ini` содержит `RustServer.PluginCatalog`, directory `plugin-catalog`, health `127.0.0.1:5116/health/ready`, и сохранил существующие регистрации;
- `nginx -t` проходит, маршрут `/plugin-catalog-api/api/...` проксируется только после Admin authorization;
- Bootstrap не скачивает и не билдит Catalog из `develop` на production VM.

Любая проблема при provisioning — **остановить цепочку**, сохранить несекретные логи, внести минимальное исправление в владеющий репозиторий и повторить тест на выделенной VM, не «подправлять руками сервер до успеха». Для DB schema НЕ применять `EnsureCreated`, `EnsureDeleted` или auto-migrations в Production. Не удалять/переименовывать существующую БД при ошибках.

## 5. Updater install/rollout (только когда есть разрешённый и опубликованный Release)

- Проверить регистрацию через Updater read-only API, совпадение релиза с установленным каналом и контракт артефакта.
- Запустить **штатный** `Prepare → Install` для `RustServer.PluginCatalog` и дождаться `Succeeded`, либо штатный Emergency Rollout, если backend commit реально установлен, все required app releases доступны и тестовый оператор разрешил Rollout. Не вызывать endpoint повторно при потере связи без сверки operation ID/status.
- Убедиться в корректном `current` в `/opt/rustserver/plugin-catalog`, `systemctl is-active rustserver-plugin-catalog`, `curl -i http://127.0.0.1:5116/health` (200 liveness) и `/health/ready` (200 только с рабочими DB/media).
- Проверить реальный listener через `ss`: только loopback, не 0.0.0.0 / внешняя IP.
- Если нет release, остановить именно rollout/E2E подэтап как **BLOCKED — missing published release**. Не ставить development output напрямую в production target и не заводить temporary release-метку вручную.

## 6. Реальная PostgreSQL + API + Media проверка

На disposable host через штатный Catalog API и разрешённую Admin-сессию, сохранив UUID тестовых записей:

1. Создать тестовый Plugin → POST; получить его через GET, добавить один Source (например `GitHub`) и проверить list/filter/search/page.
2. Загрузить реальный маленький PNG через multipart (не фиктивные байты); проверить `201`, original + `thumbnail.webp` через content endpoints, возврат правильных content-type и изображение в Web.
3. Назначить эту media главной иконкой; проверить icon в `GET /api/plugins` и details, после чего перезапустить `rustserver-plugin-catalog.service`. Убедиться, что Plugin, Source, icon и original/thumbnail **сохранились** и links в DB валидны.
4. Проверить показ на `/dashboard/plugins/catalog` и `/dashboard/plugins/catalog/:pluginId`, создание/редактирование и источники/галерею (только если Auth login и работающий Web release имеются); проверить, что внешний Video/Preview URL не fetch/proxy сервером.
5. Удалить выбранную icon штатным API: selection очищается, metadata и файлы убираются; `DELETE Plugin` с media остаётся `409`, до очистки. После удаления остальных media удалить тестовый Plugin. Не удалять никакие не-тестовые данные.
6. Прямой `psql` smoke с минимально привилегированным сервисным identity должен читать три таблицы. Не делать SQL INSERT вместо API, чтобы не обходить логику.

Для более быстрых Development-only smoke можно использовать dev Vite proxy и dev service, но **не** засчитывать это вместо production Nginx/Updater проверок.

## 7. Admin gateway, безопасность и persistence

- Анонимный `GET /plugin-catalog-api/api/plugins`, media GET и mutating `POST` должны быть отклонены Auth gate: без доступа к данным (точный status зависит от уже согласованного Auth/Nginx flow). Не слать upload с реальным содержимым до подтверждения отказа/авторизации.
- Администратор через штатный Auth cookie видит list/details и может загружать медиа. Секреты/cookies в отчёте не воспроизводить.
- HTTP `/plugin-catalog-api/api/plugins` должен попасть в **`/api/plugins`** на Catalog без double prefix; `/plugin-catalog-api` без slash — 404; Nginx `nginx -t` перед reload.
- Проверить, что размер 1–10 MiB допустимых изображений не отклоняется proxy по дефолтному 1 MiB (при тестах не генерировать большие бесполезные данные; достаточно одного файла выше 1 MiB и ниже backend лимита, если возможно).
- Повторный Bootstrap на **той же выделенной VM** (без cleanup) не изменяет записанные Catalog данные/media, схему, права доступа, runtime registry и текущий релиз. Если повторный bootstrap не поддерживает обновление каких-то изменившихся reviewed systemd files — явно зафиксировать поведение; не заменять чужие unit files.
- При наличии двух реальных опубликованных и **совместимых со схемой** Catalog releases по разрешению оператора проверить штатный Updater Update/Rollback, затем persistence. Если второго релиза нет — **NOT RUN**, а не моделировать псевдооткат. Rollback бинарников не должен откатывать содержимое DB/media; смена несовместимой схемы вообще запрещена.
- Обязательно сохранить в отчёте последствия полного удаления тестовой VM и потребность резервировать **вместе** PostgreSQL и `/var/lib/rustserver/plugin-catalog/media`: текущие Rust world snapshots не равны backup каталога.

## 8. Ограниченные исправления и документация

- Исправляй **только воспроизведённые проблемы Stage 09** в принадлежащем репозитории (Bootstrap/SQL/unit/nginx — Platform, handlers/API — Catalog, proxy/UI — Web, Rollout — Updater). Перед правками соблюдай локальные `AGENTS.md` и опубликованные API/обновляй контракт при изменениях.
- Ошибки, требующие изменения доверенной Installer supply chain (`develop` pinning), крупной миграции схемы, auth model, нового Updater workflow или release management — **отдельный согласованный блокер**, не делать неоговорённую переделку ради зелёного теста.
- Не начинать auto version checking, plugin installs, S3, MS SQL, `RustServer.DataMigration`, общий backup service. Не писать десятки новых тестов; 1–2 точечных smoke/regression на исправленный дефект, если он выявлен.
- Документируй **фактически** выполненные шаги: `RustServer.PluginCatalog/owner-view/PROJECT_PLAN.md`, `README.md`/`docs/`, при необходимости Platform `deploy/HOW_TO_INSTALL.md` и новый `docs/changelog/iteration-NNN.md` (если были существенные исправления или новая подтверждённая end-to-end capability). Если тесты не проводились — не писать 'verified'. Существующие iteration файлы не переписывать.
- Коммиты раздельно по репозиториям и только для реальных изменений; статус рабочих деревьев и конкретные SHA включить в отчёт. Не делать коммит ради статуса без реального изменения.

## 9. Требуемый финальный отчёт Stage 09

Короткая таблица **PASS / FAIL / BLOCKED / NOT RUN** для:

| Gate | Что подтверждает |
| --- | --- |
| Source/commit gate | 4 Stage 08 commits доступны на origin, включая Updater |
| Package gate | FDD tar.gz + SHA256 + linux-x64 Magick.NET, релиз выбранного канала |
| Bootstrap gate | clean Ubuntu provisioning без исходников сервисов |
| PostgreSQL gate | role/peer DB/schema SQL + verifier на реальной БД |
| Readiness gate | `/health` + `/health/ready`, только loopback listener |
| Nginx/Auth gate | `nginx -t`, `/plugin-catalog-api`, no anonymous access, Admin access |
| API/Media gate | real PostgreSQL CRUD + media roundtrip |
| Web gate | реальный UI Catalog list/details/icon/media через gateway |
| Persistence gate | данные/файлы пережили systemd restart и Bootstrap rerun |
| Updater gate | реальный Prepare/Install; Update/Rollback только с подходящими releases |
| Installer trust gate | source Bootstrap фиксирован и проверяем (известный blocker: `develop`) |

Каждый FAIL или BLOCKED — конкретная причина, команда/путь для воспроизведения, ответственный репозиторий, необходимое следующее действие. Не включать токены, пароль, session cookie или полный connection string. Отдельно обозначить два Windows-dependent Updater теста и результат их проверки на Ubuntu.

**Считать все девять этапов полностью завершёнными только при прохождении всех обязательных gates и отсутствии production-blockers.** Если упираемся в разрешение на реальный Release или доступ к VM, честно выдать итог с частично подтверждёнными шагами и тем, что требуется от владельца; не обходить ограничения и не «додумывать» успешный E2E.
