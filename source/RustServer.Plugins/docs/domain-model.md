# RustServer.Plugins — Domain Model

**Status:** Working specification  
**Purpose:** зафиксировать основные сущности и термины системы до проектирования API и UI.

---

## 1. Environment

`Environment` определяет режим работы системы.

Допустимые значения:

- `Staging`
- `Production`

Правила:

- В `Staging` доступны `Development` и `Release`.
- В `Production` доступен только `Release`.
- `Production + Development` недопустим.

---

## 2. Source

`Source` определяет, из какого Git-состояния берутся управляемые файлы.

Есть два типа Source:

### Development

Использует ветку:

```text
develop
```

одновременно в двух repository:

- Plugins
- Configs

### Release

Использует release branch вида:

```text
release/1.0
release/2.0
release/3.0
```

Release считается валидным только если ветка с одинаковым именем существует в обоих repository.

---

## 3. Git Repository

Система управляет двумя Git repository:

```text
Plugins
Configs
```

У каждого repository есть:

- local workspace path
- local branch
- local HEAD
- remote branch
- remote HEAD

`Fetch` обновляет только remote-tracking refs. `Git Sync` — отдельная
операция над текущей поддерживаемой local branch и её canonical
`origin/<branch>`; она не является Apply Source и не меняет Baseline или
Runtime files.

Локальные repository находятся вне папки Rust server.

---

## 4. Git Source State

`Git Source State` — текущее состояние выбранного Source в обоих repository.

Пример:

```text
Source: release/2.0

Plugins
  branch: release/2.0
  HEAD: AAA

Configs
  branch: release/2.0
  HEAD: BBB
```

Для Development:

```text
Source: Development

Plugins
  branch: develop
  HEAD: CCC

Configs
  branch: develop
  HEAD: DDD
```

## 5. Runtime Commit Metadata

`Runtime Commit Metadata` — данные, записываемые в каждый Git commit, который
создаёт операция Commit + Push Runtime.

Он содержит:

- operator-provided commit message как subject;
- configured Git author identity;
- `RustServer-Operation` trailer с ID operation report;
- `RustServer-Environment` trailer с validated Environment;
- `RustServer-Source` trailer с validated Source.

Если операция создаёт commit в обоих repository, metadata одинаков для обоих
commit, включая ID операции. После того как все affected repository успешно
push'нуты, эти exact created commit SHA продвигают Baseline; до этого момента,
при failure или partial Push Baseline не меняется. Metadata сама по себе не
является отдельным persistent state сервиса.

---

## 6. Baseline

`Baseline` — exact Git reference, с которым сравниваются managed Runtime files.
Он описывает состояние после successful Apply либо после fully successful
Commit + Push selected Runtime changes.

Baseline хранит:

- Environment
- Source type
- Release name, если это Release
- Plugins commit SHA
- Configs commit SHA
- время применения

Пример:

```text
Environment: Production
Source: Release
Release: 1.0
Plugins SHA: AAA
Configs SHA: BBB
Applied At: ...
```

При successful Commit + Push SHA обновляются только для repository, в которых
создан и успешно pushed Runtime commit; SHA незатронутого repository
сохраняется. Если persistent save, commit или любой Push не завершился успешно,
Baseline не продвигается. Baseline хранится постоянно и переживает restart
сервиса.

---

## 7. Runtime State

`Runtime State` — реальные управляемые файлы, которые сейчас находятся в папке Rust server.

Runtime может отличаться от Baseline.

Причины:

- Rust изменил config-файлы;
- plugin изменил config/data;
- человек изменил файл вручную;
- после запуска появились runtime changes.

---

## 8. Runtime Diff

`Runtime Diff` — разница между Baseline и реальными файлами Rust server.

Типы изменений:

- `Added`
- `Modified`
- `Deleted`

Просмотр Runtime Diff ничего не изменяет.

Runtime Diff может быть invalidated уведомлением filesystem watcher, но
уведомление означает только "Runtime files may have changed". Оно не хранит
diff, не содержит source of truth и не может сделать уже вычисленный diff
корректным или некорректным. Авторитетным ответом всегда остаётся новое
сравнение `Baseline <-> Runtime Files`.

Для отдельной записи Runtime Diff content baseline читается из SHA, сохранённого
в Baseline, а не из current local branch HEAD или Remote Git. Стороны content
могут отсутствовать для added/deleted file; Runtime содержимое может быть
binary и тогда не является text preview.

---

## 9. Source Diff

`Source Diff` — разница между текущим Baseline сервера и выбранным Git Source.

Пример:

```text
Baseline:
release/1.0
Plugins AAA
Configs BBB

Selected Source:
release/2.0
Plugins CCC
Configs DDD
```

Source Diff показывает, что изменится при `Apply to Server`.

---

## 10. Current Server Identity

Система должна уметь ответить:

```text
Что сейчас считается установленным на сервере?
```

Ответ берётся из Baseline.

Примеры:

```text
Development
```

или:

```text
Release 1.0
```

Это не означает, что runtime-файлы не изменились после применения.

---

## 11. Release

`Release` — согласованная пара release branches с одинаковым именем:

```text
Plugins: release/2.0
Configs: release/2.0
```

Release не является отдельным JSON-каталогом версий.

Источником списка Release является Git remote.

---

## 12. Release Commit

Внутри одного Release может быть несколько commit.

Пример:

```text
release/1.0

A
B
C
```

Все они всё ещё относятся к `Release 1.0`.

Hotfix — это новый commit в текущую release branch.

---

## 13. Valid Release State

Release считается валидным для использования, если:

- branch существует в Plugins;
- branch существует в Configs;
- имя branch совпадает;
- оба repository находятся в допустимом Git state;
- remote-tracking state обновлён свежим Fetch, а local branch синхронизирована
  отдельным Git Sync, когда это требуется.

Если release branch существует только в одном repository, состояние считается невалидным.

---

## 14. Workspace

`Workspace` — локальная checkout-копия Git repository, с которой работает backend.

Есть два workspace:

```text
Plugins workspace
Configs workspace
```

Backend переключает их на нужный Source:

```text
develop
```

или:

```text
release/<version>
```

Workspace не является Rust server folder.

---

## 15. Rust Server Folder

`Rust Server Folder` — папка работающего Rust server.

Именно сюда применяются managed files из выбранного Git Source.

Git repository и Rust Server Folder — разные области хранения.

---

## 16. Managed Files

`Managed Files` — файлы, которыми управляет RustServer.Plugins.

Они участвуют в:

- diff;
- apply;
- revert;
- runtime commit.

Неуправляемые файлы не должны случайно изменяться операциями Plugins service.

Файл попадает в управляемую Runtime область не только по допустимому logical
path, но и по действующим правилам локального Git workspace владельца
(Plugins/Configs). Для untracked файлов, которые Git исключает через
`.gitignore` (включая вложенные правила), даже допустимый runtime path остаётся
**unmanaged**: он не участвует в Runtime Diff, Preview, Apply, Revert и
Commit + Push. Принцип Git сохраняется: уже отслеживаемый индексом файл
остаётся managed даже при совпадении с ignore-шаблоном. Если Git-ignore
проверка недоступна, выполнение прекращается без изменений Runtime.

### Runtime mapping

Runtime capability — единственная граница работы с физическими managed files
Rust server. Identity managed file всегда содержит repository (`Plugins` или
`Configs`), repository-relative path и logical runtime-relative path; она не
содержит абсолютный deployment path.

Текущий mapping фиксирован и ограничен следующими областями:

```text
Plugins repository
  <top-level>/*.cs          -> oxide/plugins/<file>.cs

Configs repository
  config/**                 -> oxide/config/**
  data/**                   -> oxide/data/**
  lang/**                   -> oxide/lang/**
  discord/**                -> oxide/discord/**
  oxide.config.json         -> oxide/oxide.config.json
  discord.config.json       -> oxide/discord.config.json
```

Plugins `.zip` и Configs `configToolKit/**`, `.gitignore`, `.git/**` и другое
repository/tooling content не являются Runtime managed files. Runtime content
может быть binary; capability читает, сравнивает и записывает bytes, не
предполагая UTF-8 text.

Физический managed root — `<ServerRootPath>/oxide`. Path, который выходит за
его границы или проходит через symlink/reparse point, является diagnostic
error и никогда не читается, не записывается и не удаляется автоматически.

---

## 17. Persistent State

Persistent state — данные, которые должны переживать restart RustServer.Plugins.

Минимально:

- текущий Environment;
- Baseline;
- история операций;
- служебное состояние, необходимое для безопасного продолжения работы.

Git repository и сами Rust server files существуют отдельно и не являются этим state.

### Environment storage

Текущий `Environment` хранится сервисом в отдельном файле
`environment.json` в корне состояния `PluginManagement`. Путь к этому корню
задаётся обязательной конфигурацией:

```text
PluginManagement:StateRootPath
```

Если файла нет, это нормальное первое состояние и Environment считается
`Staging`; read-only запрос не создаёт файл. После явного переключения
Environment файл должен быть записан безопасной заменой через временный файл
в том же каталоге. Повреждённый файл или неизвестное значение Environment —
диагностируемая ошибка, а не неявный возврат к `Staging`.

### Operation history storage

История изменяющих операций хранится постоянно в SQLite. Она содержит
operation ID, type, status, UTC-время начала и завершения, а также полный
detailed operation report. База использует WAL и находится по configurable
пути `PluginManagement:OperationHistory:DatabasePath`; deployment Bootstrap
задаёт для него canonical absolute path вне publish directory.

Незавершённая запись после restart не означает успешную операцию. При
следующем запуске сервис помечает её status `interrupted` с диагностикой
`OperationInterrupted`. История поддерживает стабильное чтение от новых к
старым по паре `startedAt + operationId`; она не является источником Git,
Baseline или Runtime state.

---

## 18. Главное различие состояний

Нужно всегда различать четыре вещи:

```text
1. Remote Git
2. Local Git workspace
3. Server Baseline
4. Runtime files
```

Они могут отличаться друг от друга.

Пример:

```text
Remote Git HEAD      = D
Local Git HEAD       = C
Server Baseline      = B
Runtime files        = B + local runtime changes
```

Именно поэтому UI/API не должны использовать одно слово `state` без уточнения.

---

## 19. Краткая схема

```text
Remote Git
   |
   | Fetch
   v
Local Git Workspace
   |
   | Apply to Server
   v
Baseline
   |
   | Rust runtime changes
   v
Runtime Files
```

Обратное направление в Staging:

```text
Runtime Files
   |
   | Commit Runtime Changes
   v
Local Git Workspace
   |
   | Push
   v
Remote Git
```

В Production обратный commit запрещён.

---

## 20. Термины, которые используем дальше

Используем:

- Environment
- Source
- Development
- Release
- Git Repository
- Workspace
- Git Source State
- Baseline
- Runtime State
- Runtime Diff
- Source Diff
- Runtime Commit Metadata
- Managed Files

Не используем слово `Sync` как универсальное действие. `Git Sync` —
конкретная local-branch-to-`origin` операция; Git -> Server называется Apply,
а Server -> Git — Commit + Push Runtime.

---

## Итог

Этот документ отвечает только на вопрос:

> Какие сущности существуют в RustServer.Plugins и чем они отличаются?

Он специально не описывает:

- API endpoints;
- DTO;
- HTTP-коды;
- конкретные UI-компоненты;
- внутренние классы реализации.

Следующий документ после него — `operations.md`.
