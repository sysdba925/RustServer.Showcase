# RustServer.Plugins — API Contract

**Status:** Current implemented contract

**Purpose:** HTTP API для новой Plugins architecture.  
**Source of truth:** `requirements.md`, `domain-model.md`, `operations.md`.

---

# 1. GET /health

Возвращает `200 OK`, когда сервис RustServer.Plugins запущен и отвечает на
HTTP-запросы. Endpoint проверяет только доступность самого сервиса и не
обращается к Rust Dedicated Server, Git, Runtime files, database, Docker или
другим микросервисам.

### Response 200

Пустой ответ.

---

# 2. Общие состояния API

Это не endpoint. Это общая модель состояния, которую используют endpoints.

Система может находиться в одном из состояний:

```text
uninitialized
ready
degraded
```

## uninitialized

Система ещё ни разу не была применена к Rust server.

Возможные признаки:

- baseline отсутствует;
- server folder может отсутствовать;
- managed files могут отсутствовать;
- Git может быть настроен или ещё не настроен.

Это не ошибка.

## ready

Git настроен, baseline существует, система может нормально работать.

## degraded

Система уже настроена, но одна из зависимостей недоступна или состояние повреждено.

Примеры:

- Git repository недоступен;
- remote недоступен;
- authentication failed;
- local Git diverged;
- baseline повреждён;
- server folder недоступен.

---

# 3. Git readiness

Git readiness проверяется отдельно для Plugins и Configs.

Допустимые high-level значения:

```text
ready
notConfigured
unavailable
degraded
```

---

# 4. GET /api/environment

Возвращает текущий Environment и high-level initialization state.

`systemState` вычисляется read capability без Fetch и mutation: при отсутствии
baseline это `uninitialized`; при повреждённом baseline, недоступном/degraded
Git или недоступном Runtime это `degraded`; `ready` требует корректный
baseline и доступные Git и Runtime dependencies. Повреждённый persistent
Environment остаётся отдельной ошибкой `EnvironmentStateCorrupted`.

### Response 200

```json
{
  "environment": "staging",
  "systemState": "uninitialized"
}
```

---

# 5. PUT /api/environment

Переключает Environment.

### Request

```json
{
  "environment": "production"
}
```

Допустимы только значения `staging` и `production`. Переключение изменяет
только persistent Environment; оно не выбирает Source и не запускает Git,
Apply или изменение runtime-файлов.

### Response 200

```json
{
  "environment": "production"
}
```

---

# 6. POST /api/git/fetch

Явно обновляет remote Git state для Plugins и Configs.

Проверяет:

- Git установлен;
- local repository существует;
- remote настроен;
- authentication работает;
- network доступен;
- remote repository доступен;
- fetch прошёл для обоих repository.

### Response 202

```json
{
  "operationId": "..."
}
```

Fetch выполняется синхронно внутри request. Код `202` и `operationId`
сохраняют HTTP-границу. `202` возвращается после успешного Fetch обоих repository. При
частичном или полном Fetch failure возвращается общий error contract с
repository и phase; успешно обновлённые remote-tracking refs не откатываются.

Fetch выполняет только `git fetch --prune origin`. Он не изменяет local
branch или HEAD и не выполняет `pull`, merge, rebase, reset, push или
checkout. Baseline, Runtime files, Environment и selected Source также не
изменяются.

---

## 6.1 POST /api/git/sync

Синхронизирует current checked-out branch одной repository с canonical
`origin`. Request не содержит action: backend выбирает единственное безопасное
действие после fresh Fetch и повторного чтения Git state.

### Request

```json
{
  "repository": "plugins"
}
```

`repository` допускает только `plugins` или `configs`.

### Response 202

```json
{
  "operationId": "..."
}
```

### Flow and safety

```text
acquire exclusive guard
→ validate Git/Source and managed Runtime safety
→ fetch --prune origin for selected repository
→ re-read local branch, upstream, cleanliness and relation
→ equal: complete no-op
→ behind: fast-forward current branch
→ ahead: re-check remote, push current branch
→ diverged: merge origin/<branch>, re-check remote, push current branch
→ capture final Git/Baseline state and complete report
```

Sync accepts only current `develop` or `release/<version>` whose upstream is
exactly `origin/<current-branch>`. It does not switch branches. Dirty
workspaces, detached HEAD, unsupported branch, missing/invalid upstream and
unknown state return a diagnostic failure without a workspace mutation.

The command never rebases, resets or force-pushes. Before Push it fetches and
re-validates the remote state; a non-fast-forward rejection is returned as a
failure and never causes an automatic retry or force push. A regular merge uses
the configured `PluginManagement:GitCommitAuthor` only for that Git command;
it does not write Git config or Runtime commit trailers. On a merge conflict,
the command attempts `git merge --abort` and reports the recovery outcome. A
failed abort or recovery explicitly marks manual intervention as required.

Sync and Fetch never update Baseline or Runtime files, and never change
Environment or selected Source.

---

# 7. GET /api/git/status

Возвращает состояние обоих Git repository.

Endpoint не делает fetch автоматически.

### Response 200

```json
{
  "gitStatus": "ready",
  "plugins": {
    "configured": true,
    "localBranch": "develop",
    "localHead": "abc123...",
    "remoteBranch": "origin/develop",
    "remoteHead": "abc123...",
    "workspace": "clean",
    "relation": "equal",
    "diagnostic": null
  },
  "configs": {
    "configured": true,
    "localBranch": "develop",
    "localHead": "def456...",
    "remoteBranch": "origin/develop",
    "remoteHead": "def456...",
    "workspace": "clean",
    "relation": "equal",
    "diagnostic": null
  },
  "lastFetchAt": "2026-10-08T10:00:00Z"
}
```

`relation` использует `equal`, `ahead`, `behind` или `diverged`; если такое
отношение невозможно определить, оно имеет значение `unknown` и response
возвращает явный diagnostic для repository. Status читает только уже
существующие local и remote-tracking refs: он не выполняет Fetch, сетевые
запросы или local Git mutation.

`lastFetchAt` равен `null` до первого успешного explicit Fetch и после
restart сервиса. На Stage 06 он хранится только в памяти процесса,
устанавливается лишь после успешного Fetch обоих repository и не участвует в
safety-critical решениях.

---

# 8. GET /api/releases

Возвращает release branches из уже известных local remote-tracking refs
`origin/release/*` в Plugins и Configs. Endpoint сам fetch не делает, не
использует сеть и не меняет Git workspace.

Элементы сортируются по `branch` в ordinal ascending порядке.

### Response 200

```json
{
  "releases": [
    {
      "version": "2.0",
      "branch": "release/2.0",
      "status": "complete",
      "current": true,
      "matchesBaseline": true,
      "pluginsHead": "abc123...",
      "configsHead": "def456..."
    }
  ]
}
```

`status` имеет значение `complete` или `incomplete`. Для incomplete Release
отсутствующий repository HEAD возвращается как `null`; такой Release виден
диагностически, но не доступен для Apply.

`current` определяется **только** по Baseline: он равен `true`, если
существующий Baseline имеет Source `release` с той же версией. Local branch,
local HEAD и remote HEAD для этого признака не используются. Если Baseline
отсутствует, относится к Development или указывает на Release, которого нет
среди известных remote-tracking refs, ни один элемент списка не является
current.

`matchesBaseline` показывает, что оба известных remote-tracking HEAD этого
Release в точности равны Plugins и Configs SHA применённого Baseline. Для
Release, который не является `current`, и для incomplete Release это значение
равно `null`. Значение `false` не означает, что Git branch новее или старее:
endpoint не проверяет ancestry. Так UI может явно показать, что текущий
серверный Release уже отличается от известного Git state.

Если local repository или его canonical remote `origin` нельзя прочитать,
endpoint возвращает существующий Git error contract (`GitRepositoryUnavailable`
или `GitRemoteMissing`) с repository и phase `releaseDiscovery`. Эта ошибка не
превращается в искусственный incomplete Release.

Если persistent Baseline повреждён, endpoint возвращает общий error
`BaselineStateCorrupted` (500); он не продолжает release discovery с
предполагаемым или отсутствующим Baseline.

Complete Release:

```text
Plugins: release/2.0
Configs: release/2.0
```

Incomplete Release:

```text
Plugins: release/3.0
Configs: -
```

Incomplete Release можно показывать в UI, но применять нельзя.

---

# 9. GET /api/baseline

Возвращает Applied Baseline — exact Git reference для managed Runtime files.
Он создаётся successful Apply и продвигается successful Commit + Push selected
Runtime changes. Endpoint читает только persistent state и не вызывает Git или
Runtime capabilities.

### Response 200 — baseline exists

```json
{
  "status": "ready",
  "baseline": {
    "environment": "production",
    "source": "release",
    "release": "2.0",
    "pluginsCommit": "abc123...",
    "configsCommit": "def456...",
    "appliedAt": "2026-10-08T10:00:00Z"
  }
}
```

Transport values for `environment` and `source` are lowercase. `release` is
`null` for a Development baseline. `appliedAt` is the time of the successful
Apply or fully successful Commit + Push that last advanced this reference.

### Response 200 — baseline отсутствует

```json
{
  "status": "notInitialized",
  "baseline": null
}
```

Это не ошибка.

Если `baseline.json` существует, но не может быть прочитан или не описывает
корректный Baseline, endpoint возвращает `BaselineStateCorrupted` (500).
Сервис не подменяет такое состояние `notInitialized` и не удаляет файл
автоматически.

---

# 10. GET /api/runtime/diff

Managed Runtime scope is constrained by the current local Git workspace ignore
rules. Untracked paths ignored by Git are excluded before the diff is formed:
they never appear in `changes`, content comparisons or selectable
Commit/Revert entries. The same protected scope is used for Apply/Preview/Revert,
including rollback snapshots, so ignored server-owned data is not deleted.
Git-tracked files remain managed despite matching ignore patterns, consistent
with Git's index semantics. Git ignore resolution is read-only and must fail
closed on unavailable/invalid Git state; no automatic Fetch is performed.


Сравнивает runtime managed files с Baseline. Для existing Baseline managed set
восстанавливается из конкретных Plugins и Configs commit SHA, сохранённых в
Baseline; current branch, local HEAD и remote HEAD не используются как
замена. Endpoint не выполняет Fetch, checkout или другую Git mutation.

### Response 200 — baseline exists

```json
{
  "status": "ready",
  "changes": [
    {
      "repository": "configs",
      "path": "oxide/data/Example/image.png",
      "change": "modified"
    }
  ]
}
```

`change` имеет значение `added`, `modified` или `deleted`. `path` всегда
является managed logical Runtime path, а не absolute host path. Содержимое
сравнивается binary-safe способом.

### Response 200 — baseline отсутствует

```json
{
  "status": "notInitialized",
  "changes": []
}
```

Это normal first-start state. Если baseline есть, но его Git object или
Runtime недоступны, endpoint возвращает диагностическую ошибку; он не
подменяет такое состояние пустым diff и не делает automatic Fetch.

Сравнение выполняется:

```text
Baseline <-> Runtime Files
```

## 10.1 GET /api/runtime/events

Открывает Server-Sent Events stream (`text/event-stream`) для invalidation
notifications. Когда managed Runtime files могли измениться, stream получает:

```text
event: runtimeChanged
data: {}
```

Event не содержит paths, diff или persistent sequence. Filesystem events могут
быть debounce/coalesced. Изменения unmanaged paths не публикуются. Overflow
filesystem watcher buffer также публикует invalidation, потому что клиент
должен заново получить Runtime Diff; потеря event не может повлиять на
корректность `GET /api/runtime/diff`.

Endpoint не читает Baseline/Git/Runtime content, не выполняет Fetch, checkout
или любую mutation и доступен при running Rust server. Closing/cancelling
HTTP request освобождает SSE subscription.

## 10.2 GET /api/runtime/diff/content

Лениво возвращает content конкретной текущей Runtime Diff entry.

### Query parameters

```text
repository = plugins | configs
path       = managed logical Runtime path, for example oxide/config/Test.json
```

`repository` и `path` обязаны описывать одну допустимую `ManagedFileMapping`.
Absolute paths, traversal, backslashes, unmanaged paths и repository/path
mismatch отклоняются. Endpoint сверяет entry с current `Baseline <-> Runtime`
comparison: отсутствующая или уже unchanged entry возвращает
`RuntimeDiffEntryNotFound`.

### Response 200 — baseline exists and entry changed

```json
{
  "status": "ready",
  "content": {
    "repository": "configs",
    "path": "oxide/config/Test.json",
    "change": "modified",
    "baseline": { "kind": "text", "text": "{\"enabled\":false}" },
    "runtime": { "kind": "text", "text": "{\"enabled\":true}" }
  }
}
```

Each side has `kind` equal to `missing`, `text`, or `binary`. `text` has a
UTF-8 `text` value; `missing` and `binary` have `text: null`. Added entries
have a missing baseline side and deleted entries have a missing Runtime side.
Binary or invalid UTF-8 content is returned as `binary` and is not converted
to presentation HTML or a text preview.

Baseline content is read from the exact Plugins/Configs SHA saved in Baseline,
not current local HEAD, remote HEAD or branch name. This is a read-only
endpoint: no Fetch, checkout, Git mutation, Runtime mutation or Baseline
write occurs, and it may run while the Rust server is running.

### Response 200 — baseline отсутствует

```json
{
  "status": "notInitialized",
  "content": null
}
```

This is the same normal first-start state as Runtime Diff. Corrupted Baseline
and unavailable baseline Git objects or Runtime preserve the Runtime Diff
error contract.

---

# 11. POST /api/source/preview

Показывает точный предполагаемый результат Apply выбранного Source в
управляемых Runtime files. Это read-only snapshot уже известного Git state:
endpoint не выполняет Fetch, checkout, fast-forward, mutation Git workspace,
Runtime, Baseline или Environment; он не создаёт Operation Report и не требует
остановки Rust server.

### Request

Оба Source используют единую transport-модель:

```json
{
  "source": "development"
}
```

или:

```json
{
  "source": "release",
  "release": "2.0"
}
```

`source` допускает только lowercase `development` или `release`.
`development` не принимает `release`; `release` требует непустую версию.
Невалидная transport-модель возвращает `InvalidSource` (400). Доменная модель
Source остаётся единой для Preview и Apply.

### Resolution and comparison

Target resolves only from already present local Git object databases and
remote-tracking refs:

```text
Development  -> origin/develop
Release      -> origin/release/<version>
```

для Plugins и Configs. Current checked-out HEAD не используется как substitute
для другого Target Source. Release доступен только при наличии одной и той же
remote-tracking branch в обоих repository; branch в одном repository даёт
`ReleaseIncomplete` (409) с `missingRepositories`.

Preview compares:

```text
Target Git Source <-> Runtime Files
```

Это не Runtime Diff (`Baseline <-> Runtime Files`). В comparison входят только
managed files и их binary-safe contents. Unmanaged repository files не
возвращаются. Preview не является token или authorization для Apply: Apply
всегда выполняет собственный fresh Fetch и повторную validation.

### Response 200

```json
{
  "environment": "staging",
  "currentSource": {
    "type": "development",
    "release": null
  },
  "currentSourceDiagnostic": null,
  "baseline": null,
  "targetSource": {
    "type": "release",
    "release": "2.0"
  },
  "targetGit": {
    "pluginsHead": "abc123...",
    "configsHead": "def456..."
  },
  "counts": {
    "added": 3,
    "modified": 5,
    "deleted": 2
  },
  "changes": [
    {
      "repository": "configs",
      "path": "oxide/config/Test.json",
      "change": "modified"
    }
  ]
}
```

`currentSource` равно `null`, если два local workspaces не описывают один
поддерживаемый Source. В этом случае `currentSourceDiagnostic` возвращает
observed branches для Plugins и Configs; endpoint не угадывает Source.
`baseline: null` — normal first-start state.

`Production + Development` возвращает `SourceNotAllowed` (409) и не меняет
Environment. Отсутствующий known Target ref возвращает
`TargetSourceUnavailable` (409); это не вызывает network access.

Target Source разрешается только по literal remote-tracking ref:
`refs/remotes/origin/develop` или
`refs/remotes/origin/release/<version>`. Значение `release` не
интерпретируется как Git revision expression.

Endpoint не обязан делать новый fetch.

---

# 12. POST /api/source/apply

Подтверждённо применяет Target Source из той же request-модели, что и Preview.
Apply выполняется синхронно внутри request; `202` обозначает успешно
завершённую операцию и возвращает её report id.

### Request

```json
{
  "source": "development"
}
```

или:

```json
{
  "source": "release",
  "release": "2.0"
}
```

Validation transport и Environment/Source policy идентичны Preview. Apply не
принимает Preview token или session: snapshot Preview может устареть.

### Response 202

```json
{
  "operationId": "..."
}
```

После success `GET /api/operations/{operationId}` возвращает completed Apply
report. При failure после начала операции HTTP error содержит `operationId`, и
тот же endpoint возвращает полный failed report.

До начала mutation guard или server precondition rejection может не создавать
operation history entry.

Flow:

```text
acquire exclusive guard
→ validate Git/Source and managed Runtime safety
→ read Environment and validate Target Source
→ capture previous Baseline and local workspace states
→ begin report
→ fresh fetch Plugins
→ fresh fetch Configs
→ re-resolve and validate Target Source from fetched remote-tracking refs
→ validate both local workspaces
→ prepare Plugins and Configs workspaces
→ calculate exact managed target set from validated commit SHAs
→ apply Runtime additions/modifications/deletions
→ persist Baseline with those exact SHAs
→ capture final Git and Baseline state
→ complete report and release guard
```

Fresh fetch is performed directly through RemoteGit capabilities; Apply does
not invoke the Fetch Git command handler. If one Fetch succeeds and the other
fails, already-updated remote-tracking refs are reported and are not hidden;
Runtime and Baseline remain unchanged.

Local workspace preparation requires a valid, clean Git worktree. Target local
branches track their matching `origin/<branch>`; an absent target branch may
be created from the validated remote-tracking ref, equal branches may be used,
and behind-only branches may only fast-forward. Ahead or diverged target
branches, dirty worktrees, forced checkouts, resets, cleans, merges and rebases
are not used. If the linked two-repository preparation partially succeeds,
Apply attempts to restore the already-prepared workspace and records the
outcome.

Apply reads managed target files as bytes from the exact resolved commits and
makes Runtime managed files exactly match that set. It deletes obsolete files.
Before Runtime mutation it captures an operation-scoped rollback snapshot. A
Runtime write failure, or Baseline save failure after Runtime mutation, attempts
Runtime rollback and workspace rollback. A Baseline is saved only after the
Runtime apply succeeds; its Environment, Source and commit SHAs are the exact
applied values, not inferred from a later workspace HEAD.

---

# 13. POST /api/runtime/commit

Commit + Push Runtime changes выполняется только в `Staging`. Source и target
branch определяются текущим Baseline: `development` коммитится в `develop`, а
`release/<version>` — прямо в ту же release branch. Production всегда
возвращает `CommitNotAllowedInProduction`. Операция не создаёт hotfix, fix или
temporary branches.

### Request

```json
{
  "message": "Fix Example plugin configuration",
  "changes": [
    {
      "repository": "plugins",
      "path": "oxide/plugins/Example.cs"
    },
    {
      "repository": "configs",
      "path": "oxide/config/Example.json"
    }
  ]
}
```

`message` должен содержать непустой текст. `changes` должен быть непустым
массивом без duplicate пары `repository` + `path`. `repository` допускает
только `plugins` или `configs`; `path` — только logical managed Runtime path,
не абсолютный filesystem path. Repository и path должны соответствовать
фиксированному `ManagedFileMapping`.

`message` сохраняется в созданном Git commit как operator-provided subject;
сервис не подменяет его generic message. После subject сервис добавляет:

```text
RustServer-Operation: <operationId>
RustServer-Environment: staging
RustServer-Source: development | release/<version>
```

`operationId` — actual ID report, созданного для данного запроса. Если Commit
+ Push создаёт commit и в Plugins, и в Configs, оба содержат один и тот же ID.
Environment и Source берутся из validated command state. Identity author
задаётся только конфигурацией `PluginManagement:GitCommitAuthor:Name` и
`PluginManagement:GitCommitAuthor:Email`; service передаёт её только созданной
Git command и не изменяет global или repository Git configuration.

Каждый selected change обязан существовать в текущем Runtime Diff
(`Baseline <-> Runtime`). Selection ID или server-side selection session не
создаются. Невыбранные Runtime changes не записываются, не stage'ятся и
остаются в Runtime.

### Response 202

```json
{
  "operationId": "..."
}
```

`202` возвращается только после успешного Commit + Push всех затронутых
repository. При failure после создания operation report HTTP error содержит
`operationId`; полный report доступен по `GET /api/operations/{operationId}`.

### Flow and safety

```text
acquire exclusive guard
→ validate Git/Source and managed Runtime safety
→ verify Staging and valid Baseline
→ fresh fetch Plugins and Configs
→ verify Baseline Source is still available and both remote HEAD equal Baseline SHA
→ verify each local Source workspace is already synchronized at those exact
  Baseline commits; select/create a branch only when no existing branch needs
  advancement
→ read Runtime Diff and validate selected changes
→ snapshot selected Runtime contents and write/delete only those snapshots in
  their owning workspace
→ stage and verify only selected repository paths
→ create one commit per affected repository
→ push origin <baseline-source-branch> per affected repository
→ persist Baseline with created commit SHA for affected repositories
→ capture final Git/Baseline state, complete report, release guard
```

Fresh remote heads that differ from the Baseline SHA return
`GitSourceChangedSinceBaseline`; the service does not merge, rebase or apply a
Runtime delta over that newer/older Source. The operator must use normal
Preview + Apply first.

If the local Baseline Source branch is `behind`, `ahead` or `diverged`, Commit
Runtime returns `GitSynchronizationRequired`. It does not fast-forward, merge,
pull, rebase or reset; the operator must run the separate Git Sync operation
first. A selected Runtime snapshot is committed rather than bytes re-read later
from Runtime. If the selection becomes invalid while its snapshot is captured,
the command fails rather than silently committing a later unvalidated version.

Before the first push, failure attempts to restore only operation-created
working-tree changes and unpushed commits. After one repository has pushed,
the operation is not distributed-atomic: an already pushed remote commit is
never force-rolled-back. Remaining unpushed operation-created commits are
still rolled back when safe and the report records the partial remote success
and final local states. Push uses only canonical `origin` and never uses force.

Only after every affected repository push succeeds, the command persists a new
Baseline. It preserves Environment and Source, replaces the commit SHA of each
affected repository with its exact created commit SHA, and preserves the prior
SHA of an unaffected repository. Runtime files are not modified. Therefore a
successful Commit + Push immediately removes selected changes from Runtime Diff
without a separate Apply; unselected changes remain in the diff.

Commit, Push, partial-push, cancellation, or Baseline-persistence failure never
advances Baseline. If persistence fails after successful remote Push, the
operation fails, the already pushed commits are not force-rolled-back, and the
previous Baseline remains the report's final Baseline.

After a report has started, cancellation stops the active Git child process,
runs cleanup and final-state capture with an independent token, marks the
report failed with `OperationCanceled`, then releases the exclusive guard.

---

# 14. POST /api/runtime/revert

Возвращает managed Runtime files ровно к существующему Baseline:

```text
Baseline Git objects -> Runtime Files
```

Endpoint не использует current branch, local HEAD или Remote Git как замену
Baseline. Он читает managed files из exact Plugins и Configs commit SHA,
сохранённых в Baseline, и не выполняет Fetch, pull, checkout, switch,
fast-forward или push. Local Git workspace и Baseline не изменяются.

### Request

Старый вызов без request body сохраняет режим **Revert all**: он восстанавливает
весь Managed Runtime. Для **Revert selected** передаётся тот же формат file
selection, что и в `POST /api/runtime/commit`:

```json
{
  "changes": [
    {
      "repository": "configs",
      "path": "oxide/config/Economics.json"
    }
  ]
}
```

`changes` в переданном body должен быть непустым массивом без duplicate пары
`repository` + `path`. `repository` допускает только `plugins` или `configs`;
`path` должен быть exact logical managed Runtime path, соответствующим
`ManagedFileMapping`, и не может быть абсолютным или unsafe filesystem path.
Каждый selected entry обязан существовать в текущем Runtime Diff. Empty,
malformed, duplicate, unmanaged или missing selection возвращает
`InvalidRuntimeSelection` и никогда не переключает операцию в Revert all.

В selective mode восстанавливаются только выбранные entries: Added удаляется,
Modified перезаписывается Baseline bytes, Deleted создаётся с Baseline bytes.
All unselected Runtime files remain untouched. Baseline Git objects остаются
единственным source восстановления; current branch, local HEAD и Remote Git не
используются.

### Response 202

```json
{
  "operationId": "..."
}
```

Flow:

```text
acquire exclusive guard
→ validate Git/Source and managed Runtime safety
→ require a valid Baseline
→ read exact managed files from Baseline Git objects
→ snapshot the managed Runtime files in the Revert scope
→ restore all Runtime additions/modifications/deletions, or only selected entries, to Baseline
→ capture final Git and Baseline state
→ complete report and release guard
```

An added Runtime file is deleted, a modified file is overwritten with the
Baseline bytes, and a deleted file is recreated from the Baseline bytes.
Contents are binary-safe. A Runtime that already matches Baseline is a
successful idempotent operation with zero changes.

Before the first Runtime mutation the handler keeps a command-scoped snapshot
of the Revert scope (all managed files for Revert all, selected files for
Revert selected). A Runtime failure or cancellation attempts to restore that
snapshot with an independent cancellation token, then captures final state and
finishes the report before releasing the guard. Revert never writes a new
Baseline or `appliedAt`.

### Errors

```text
InvalidRuntimeSelection       400 — request selection is empty, malformed, duplicate, unmanaged, unsafe or no longer exists in Runtime Diff.
BaselineNotInitialized       409 — no Baseline exists; Git HEAD is not used as a substitute.
BaselineStateCorrupted       500 — existing baseline.json is invalid.
BaselineGitObjectUnavailable 503 — an exact Baseline commit/blob cannot be read locally.
RuntimeUnavailable           503 — managed Runtime root is unavailable or unsafe.
RuntimeRevertFailed          500 — Runtime restoration did not complete; the report records rollback.
OperationCanceled            500 — cancellation after report start completed rollback/final capture.
```

The common mutation precondition error `OperationInProgress` still applies.
Rust lifecycle status never blocks a PluginManagement mutation.

---

# 15. GET /api/operations

Возвращает compact persistent operation history. Query parameters:

- `limit` optional, default `20`, integer from `1` through `200`;
- `cursor` optional opaque cursor from the previous response.

Sorting is newest to oldest by `startedAt` and then `operationId`. A cursor
continues strictly after that pair, so operations added between page requests
do not duplicate or shift already returned entries.

### Response 200

```json
{
  "items": [
    {
      "operationId": "...",
      "type": "revertRuntime",
      "status": "succeeded",
      "startedAt": "2026-10-10T10:15:00Z",
      "completedAt": "2026-10-10T10:15:03Z",
      "changedFiles": 3
    }
  ],
  "nextCursor": null
}
```

`nextCursor` is `null` when no older entry exists. Invalid `limit` or cursor
returns `400 InvalidOperationHistoryQuery`. The list intentionally excludes
the detailed report; clients use the endpoint below for an individual entry.

---

# 16. GET /api/operations/{operationId}

Возвращает полный detailed operation report из persistent history, включая
записи, созданные до restart сервиса.

### Response 200

```json
{
  "operationId": "...",
  "type": "fetchGit",
  "status": "succeeded",
  "startedAt": "2026-10-10T10:15:00Z",
  "completedAt": "2026-10-10T10:15:03Z",
  "currentPhase": "completed",
  "source": null,
  "steps": [
    { "name": "fetchPlugins", "status": "completed" },
    { "name": "fetchConfigs", "status": "completed" },
    { "name": "captureFinalState", "status": "completed" }
  ],
  "failure": null,
  "rollback": { "status": "notAttempted", "message": null },
  "counts": { "added": 0, "modified": 0, "deleted": 0 },
  "changes": [],
  "sync": null,
  "finalGitState": [
    {
      "repository": "plugins",
      "branch": "develop",
      "head": "abc123...",
      "relation": "equal",
      "diagnostic": null
    }
  ],
  "previousBaseline": null,
  "finalBaseline": null
}
```

`type` имеет значения `fetchGit`, `gitSync`, `applySource`, `commitRuntime`
или `revertRuntime`; `status` — `inProgress`, `succeeded`, `failed` или
`interrupted`; status
step — `completed`, `failed` или `notCompleted`. В Fetch применимы только
реальные Git steps, `rollback` всегда `notAttempted`, а Baseline не
создаётся и не подменяется. Failed report сохраняет failure context и
фактически доступное final Git state.

Для `gitSync` поле `sync` не равно `null` и содержит `repository`,
`initialRelation`, выбранное `behavior` (`none`, `fastForward`, `push` или
`mergeAndPush`) и `manualInterventionRequired`. Этот report также содержит
phase/steps, final Git state, push result в соответствующем step и outcome
merge recovery в `rollback`/failure context.

Недостаточно возвращать только:

```text
failed
```

Report должен содержать конкретную фазу, repository, file path, reason, rollback и final state.

Для Apply `counts` и `changes` описывают exact managed Runtime transition.
`changes` использует `repository`, logical managed `path` и `change` (`added`,
`modified` или `deleted`). Для других operation types их значения могут быть
пустыми/нулевыми, если такие файлы неприменимы.

For successful `commitRuntime`, `previousBaseline` is the reference used to
validate the selected Runtime Diff and `finalBaseline` contains the persisted
created commit SHA for every affected repository. For failed or partially
successful `commitRuntime`, `finalBaseline` remains the previous Baseline.

Все detailed reports включают `startedAt` (UTC) и `completedAt` (UTC или
`null` для текущей in-progress operation). После restart каждая сохранённая
`inProgress` operation получает `interrupted`, `completedAt` и failure code
`OperationInterrupted`; сервис не пытается автоматически repair Git, Runtime
или Baseline.

---

# 17. Общий Error Contract

Все endpoints используют единый формат ошибки:

```json
{
  "code": "GitAuthenticationFailed",
  "message": "Authentication to the Git remote failed.",
  "details": {
    "repository": "plugins",
    "phase": "fetch"
  }
}
```

Основные группы:

```text
Configuration
Git local
Git remote
Release
Baseline
Server
```

Для Environment дополнительно определены:

```text
InvalidEnvironment          400 — request содержит значение, отличное от staging или production.
EnvironmentStateCorrupted   500 — сохранённый Environment отсутствует в корректном формате или содержит неизвестное значение.
```

При `EnvironmentStateCorrupted` сервис не должен подменять повреждённое
состояние значением `staging` и не выполняет автоматическое восстановление.

Для Baseline определено:

```text
BaselineStateCorrupted  500 — baseline.json не находится в корректном формате,
                              содержит неизвестные Environment/Source,
                              inconsistent Release/Source, invalid commit SHA
                              или обязательные данные отсутствуют.
```

При `BaselineStateCorrupted` сервис не должен возвращать `notInitialized` или
удалять persistent file автоматически.

Для Runtime Diff дополнительно определены:

```text
BaselineGitObjectUnavailable  503 — commit из Baseline нельзя прочитать из
                                  local Git object database.
RuntimeUnavailable            503 — managed Runtime root недоступен или
                                  небезопасен.
InvalidRuntimeDiffEntry       400 — repository/path не описывают допустимую
                                  managed Runtime identity.
RuntimeDiffEntryNotFound      404 — допустимая запись больше не является
                                  current Runtime Diff entry.
```

Для Source Preview дополнительно определены:

```text
InvalidSource              400 — request не описывает Development или Release
                             с корректной версией.
SourceNotAllowed           409 — Target Source запрещён для текущего Environment.
ReleaseIncomplete          409 — release remote-tracking branch отсутствует
                             в одном из repository; details содержит
                             missingRepositories.
TargetSourceUnavailable    409 — known remote-tracking ref Target Source
                             отсутствует в обоих repository или недоступен.
```

Для mutation commands определены общие safety errors:

```text
OperationInProgress      409 — другая PluginManagement mutation уже выполняется.
```

Эти ошибки являются precondition rejection: command не начинает mutation и
может не создавать operation history entry.

Для Apply Source дополнительно определены:

```text
InvalidSource              400 — request не описывает допустимый Target Source.
SourceNotAllowed           409 — Target Source запрещён текущим Environment.
ReleaseIncomplete          409 — после fresh Fetch release отсутствует в одном
                             repository; details содержит missingRepositories.
TargetSourceUnavailable    409 — validated remote-tracking Target ref отсутствует.
GitWorkspaceUnsafe         409 — local worktree/target branch не удовлетворяет
                             безопасным правилам preparation.
SourcePreparationFailed    409 — безопасная preparation/checkout target Source
                             не завершилась.
RuntimeApplyFailed         500 — managed Runtime mutation не завершилась;
                             details содержит operationId и report описывает rollback.
BaselinePersistFailed      500 — Baseline не сохранён после Runtime apply;
                             details содержит operationId и report описывает rollback.
OperationCanceled          500 — request был отменён после начала Apply;
                             operation report завершается после rollback и capture
                             final state. Клиент мог не получить этот response.
```

После создания Apply operation report отмена останавливает активный Git process
и ожидает его фактического завершения. Затем сервис выполняет failure
cleanup/rollback и capture final state с независимым cancellation token, отмечает
report как failed с `OperationCanceled` и только после этого освобождает
exclusive mutation guard.

Для Commit + Push Runtime дополнительно определены:

```text
CommitNotAllowedInProduction  409 — current Environment не является Staging.
BaselineNotInitialized        409 — отсутствует Baseline для определения Source.
NoRuntimeChangesSelected      400 — request не содержит selected changes.
InvalidRuntimeSelection       400 — message/selection некорректны, содержат duplicate,
                                   неподдерживаемый repository/path или отсутствуют в Runtime Diff.
GitRepositoryUnavailable      409 — local workspace или Git CLI, нужные для проверки
                                   Baseline Source или Push, недоступны.
GitRemoteMissing              409 — canonical origin отсутствует при Fetch или Push.
GitRemoteFetchFailed          502 — Fetch завершился без надёжного более узкого диагноза.
GitSourceChangedSinceBaseline 409 — после fresh Fetch Source HEAD не равен применённому Baseline SHA.
GitSynchronizationRequired    409 — local Baseline Source branch требует отдельного Git Sync;
                                   Commit Runtime не выполняет hidden fast-forward или merge.
GitWorkspaceUnsafe            409 — workspace нельзя безопасно подготовить или staged/working set
                                   не соответствует selected paths.
GitCommitFailed               500 — commit, включая repository commit identity, не создан.
GitPushFailed                 502 — canonical origin push не завершился; report отражает partial success.
BaselinePersistFailed         500 — all selected pushes succeeded, но новый Baseline не сохранён;
                                   remote commit не откатывается force push, Baseline остаётся прежним.
OperationCanceled             500 — после создания report request отменён; cleanup и final state
                                   завершены до освобождения guard.
```

Runtime Diff dependency errors сохраняют существующие `BaselineGitObjectUnavailable`
и `RuntimeUnavailable`; failure fetch сохраняют существующие Git error codes.
`GitSourceChangedSinceBaseline` используется только когда оба current known
remote-tracking SHA успешно прочитаны, но хотя бы один отличается от Baseline;
недоступность Git repository/CLI не подменяется этим кодом. Push сохраняет
известные статусы workspace/not-repository как `GitRepositoryUnavailable` и
missing `origin` как `GitRemoteMissing`; network/auth/remote rejection остаётся
`GitPushFailed`.

Для persistent operation history определено:

```text
OperationNotFound             404 — operationId отсутствует в persistent history.
OperationHistoryUnavailable   503 — SQLite history недоступна, повреждена или
                                    final report нельзя сохранить.
InvalidOperationHistoryQuery  400 — limit или cursor endpoint списка некорректен.
```

Operation reports persist across restart. После crash/restart service только
помечает сохранённые unfinished reports как `interrupted`; он не пытается
автоматически repair Git, Runtime или Baseline. Operator recovers through
observable Git Status, Baseline, Runtime Diff and Environment/SystemState.

Для Git endpoints определены:

```text
GitRepositoryUnavailable  — local workspace отсутствует, не является Git repository или Git CLI недоступен.
GitRemoteMissing          — canonical remote origin отсутствует в local repository.
GitRemoteFetchFailed      — Fetch canonical remote не завершился; узкая причина не предполагается без надёжного сигнала Git.
GitSyncWorkspaceUnsafe    409 — branch, upstream, workspace или observed relation небезопасны для Sync.
GitSyncFailed             502 — fast-forward, merge или Push не завершились; report содержит final state.
GitRemoteChanged          409 — remote изменился после выбора Sync action либо refreshed tracking state
                              надёжно подтвердил изменение remote после failed Push; повторите Fetch/Sync
                              без force push.
GitMergeConflict          409 — regular merge получил conflict; report содержит abort/recovery outcome.
GitMergeRecoveryFailed    500 — после failed/cancelled merge проверка или abort/recovery не восстановили
                              исходные branch/HEAD и clean workspace; требуется manual intervention.
```

---

# 18. Fresh Fetch Rule

Fresh fetch обязателен перед:

```text
Apply Source
Git Sync
Commit Runtime Changes
Commit + Push
Switch Release
Rollback Release
```

Read-only endpoints не обязаны автоматически fetch.

---

# 19. TODO / Future — Create Release

В будущем:

```text
POST /api/releases
```

может создавать одинаковую:

```text
release/<version>
```

branch в Plugins и Configs.

До реализации Release создаётся внешними Git-инструментами.

---

# 20. TODO / Required — Plugin Updater

Plugin Updater является обязательной частью `RustServer.Plugins`, но его API и storage пока не проектируются.

Доступность:

```text
Environment: Staging
Source: Development
```

В Production updater недоступен.

## Цель

Упростить обновление стороннего Rust plugin до новой версии.

Входные package могут быть разными:

```text
single .cs file
.zip
other supported archive/package
```

Формат стороннего package нельзя считать надёжным или стандартным.

Разные разработчики могут:

- использовать разную структуру каталогов;
- класть script и configs в разные места;
- добавлять language/localization files;
- использовать собственную структуру archive.

Поэтому автоматическое распознавание package не должно предполагать один универсальный vendor format.

## Предполагаемый flow

```text
Upload package
      ↓
Inspect / Parse
      ↓
Show detected files
      ↓
User verifies/mapping if required
      ↓
Apply files to Development workspace
      ↓
Commit + Push to develop
      ↓
Apply Development to Staging server
      ↓
Run Rust server
      ↓
Collect runtime config changes
      ↓
Review Runtime Diff
      ↓
Commit + Push selected runtime changes
```

## File routing

Минимально система должна уметь определить или получить от пользователя mapping:

```text
plugin script     -> Plugins repository
configs           -> Configs repository
language files    -> соответствующий managed area
other files       -> review required
```

Если package неоднозначный, система не должна молча угадывать destination.

## Future Plugin Catalog

В будущем Plugins UI должен иметь каталог установленных plugins.

Для plugin желательно хранить/показывать:

```text
plugin identity
installed version
available version
update available
source/provider
last update
```

Где именно хранится эта metadata, пока не определено.

## Future updater capabilities

TODO:

- package upload;
- archive inspection;
- safe extraction;
- file classification;
- manual mapping when auto-detection uncertain;
- preview changes;
- commit + push into `develop`;
- plugin version tracking;
- available version tracking;
- update notifications;
- integration with runtime diff after first server start.

Это обязательный future scope, но не блокирует первую реализацию новой Git/Release architecture.

---

# Minimal API

```text
GET  /api/environment
PUT  /api/environment

GET  /health

POST /api/git/fetch
POST /api/git/sync
GET  /api/git/status

GET  /api/releases

GET  /api/baseline
GET  /api/runtime/diff
GET  /api/runtime/events
GET  /api/runtime/diff/content

POST /api/source/preview
POST /api/source/apply

POST /api/runtime/commit
POST /api/runtime/revert

GET  /api/operations
GET  /api/operations/{id}
```

Plugin Updater endpoints будут добавлены отдельным блоком после стабилизации core API.
