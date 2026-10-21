# RustServer.Plugins — Operations

**Status:** Working specification  
**Purpose:** описать действия системы над доменной моделью до проектирования API и UI.

---

## 1. Get Environment
Показать текущий Environment: `Staging` или `Production`.

## 2. Switch Environment
Переключить `Staging <-> Production`.

## 3. Fetch Git
Явно обновить remote-состояние для Plugins и Configs:
- выполнить `git fetch --prune origin`;
- обновить remote-tracking branches и HEAD;
- обновить известную информацию о `develop` и release branches;
- вернуть подробный результат.

Fetch не изменяет local branch/HEAD, не делает checkout, merge, rebase,
reset, pull или push и не меняет Baseline, Runtime files, Environment либо
selected Source. Он допускается при работающем Rust server и требует
exclusive mutation guard, как и остальные Git workspace mutations.

## 3.1. Sync Git Repository
Синхронизировать одну выбранную repository (`Plugins` или `Configs`) с её
canonical `origin` remote без неявного переключения branch.

Flow:

```text
acquire exclusive guard
  ↓
validate Git workspace and remote safety
  ↓
fetch --prune origin выбранной repository
  ↓
re-read branch, tracking, cleanliness and relation
  ↓
equal   → report no-op
behind  → fast-forward local branch
ahead   → re-check remote, then push local commits
diverged→ regular merge origin/<branch>, re-check remote, then push
```

Sync поддерживает только checked-out `develop` или `release/<version>` с
upstream, точно равным `origin/<branch>`. Dirty workspace, detached HEAD,
unsupported branch, missing/invalid upstream, `unknown` state или failed
Fetch отклоняются без workspace mutation. Sync не использует rebase, reset,
force push или automatic branch switch.

После failed или cancelled regular merge система проверяет, остался ли
незавершённый merge. При наличии незавершённого merge она выполняет только
`git merge --abort`, затем проверяет восстановление original branch/HEAD и
clean workspace. Если merge уже не незавершён, она всё равно проверяет это
исходное состояние. Если проверка или abort/recovery не удались, report
возвращает `GitMergeRecoveryFailed` и явно требует manual intervention.
Sync не использует reset, rebase, force push или иной destructive cleanup;
локальные commits сохраняются. Merge commit при необходимости использует
configured GitCommitAuthor только command-scoped; ему не добавляются Runtime
commit trailers.

После failed Push Sync выполняет diagnostic Fetch. Только когда refreshed
tracking state надёжно показывает, что remote опередил или разошёлся с local
branch, он возвращает `GitRemoteChanged`; при отсутствии такого сигнала
сохраняется `GitSyncFailed`. Fetch и Sync никогда не меняют Baseline, Runtime
files, Environment или selected Source.

## 4. Get Git Status
Для обоих repository показать:
- local branch;
- local HEAD;
- remote branch;
- remote HEAD;
- clean / dirty;
- ahead / behind / diverged;
- last fetch/update result.

## 5. Get Current Source
Показать Source локальных Git workspace:
- `Development`;
- или `Release <version>`.

Это не означает, что Rust server уже в том же состоянии.

## 6. List Releases
После Fetch получить release branches из обоих remote repository.

### Complete
```text
Plugins: release/2.0
Configs: release/2.0
```
Такой Release можно использовать.

### Incomplete
```text
Plugins: release/2.0
Configs: -
```
или наоборот.

Incomplete Release можно показать в UI как предупреждение, но применять нельзя.

## 7. Validate Release Availability
Перед использованием Release проверить:
- branch есть в Plugins;
- branch есть в Configs;
- имена совпадают;
- Git state допустим.

Если Release incomplete:
- ничего не переключать;
- сервер не менять;
- вернуть понятную ошибку.

## 8. Select Target Source
Выбрать:
- `Development`;
- или конкретный `Release`.

На этом этапе ничего не менять.

## 9. Preview Source Change
Показать:
- текущий local Source;
- server baseline;
- Target Source;
- Plugins HEAD;
- Configs HEAD;
- сколько файлов added / modified / deleted / moved;
- список файлов.

## 10. Apply Source Change
Связная операция:
1. получить exclusive mutation guard;
2. проверить Target Source;
3. проверить Git state;
4. переключить Plugins workspace;
5. переключить Configs workspace;
6. применить Source к Rust server;
7. удалить obsolete managed files;
8. записать новые/изменённые managed files;
9. обновить baseline;
10. сформировать подробный report.

Нельзя оставлять отдельный checkout без завершения перехода сервера.

```text
Select
  ↓
Preview
  ↓
Confirm
  ↓
Checkout + Apply + Baseline
```

## 11. Cancel Source Change
До подтверждения Apply ничего не менять.

## 12. Get Baseline
Показать последнее успешно применённое состояние:
- Environment;
- Source;
- Release name;
- Plugins SHA;
- Configs SHA;
- applied timestamp.

## 13. Get Runtime Diff
Scope: only files managed by the current local Git workspace are eligible for
Runtime Diff. Git-ignored *untracked* paths are excluded before comparison with
Baseline; these paths do not appear as added/modified/deleted, cannot be selected
for Commit + Push/Revert, and are not deleted by Apply. Use Git's own ignore
resolution (including negation and nested rules), never a bespoke glob parser.
Already tracked Git files remain managed even if a later pattern matches them.
A Git-ignore evaluation error blocks the operation instead of including all files.

This same exclusion policy applies to Source Preview, Apply, full Revert and
rollback snapshots. Git-ignored server-owned Runtime data must be preserved
rather than treated as an obsolete managed file.

Сравнить runtime files с baseline:
- added;
- modified;
- deleted;
- moved/renamed, если определяется;
- точные пути файлов.

Diff можно смотреть при запущенном сервере.

Runtime filesystem watcher может послать клиенту coalesced notification, что
managed Runtime files могли измениться. Клиент использует его только как
сигнал заново вызвать Get Runtime Diff; watcher не вычисляет diff и не
сохраняет notification state. Overflow watcher buffer или потеря notification
никогда не меняют алгоритм либо корректность Get Runtime Diff.

Для выбранной существующей записи diff отдельная read-only операция возвращает
baseline и Runtime content. Baseline content всегда читается из сохранённого
SHA. Она не выполняет Fetch, checkout, Git/Runtime/Baseline mutation и может
работать при запущенном Rust server.

## 14. Preview Apply
Показать точный результат Git -> Server до изменения файлов.

## 15. Apply Git to Server
Привести managed files Rust server к точному состоянию Source.

Это не copy-over.

Если Git:
```text
A.cs
C.cs
```

а Server:
```text
A.cs
B.cs
```

после Apply:
```text
A.cs
C.cs
```

`B.cs` удаляется.

## 16. Handle Deleted and Moved Files

### Deleted
Если managed file отсутствует в Source, но есть на сервере — удалить.

### Moved / Renamed
Минимально допустимо:
```text
delete old/path/File.cs
add new/path/File.cs
```

Главное: после Apply managed file set сервера точно соответствует Source.

## 17. Apply Result Report
Apply / rollback / revert / Source Change / Git Sync должны возвращать подробный report:

- operation id;
- source;
- previous baseline;
- new baseline;
- added / modified / deleted / moved counts;
- список файлов;
- start time;
- finish time;
- final status.

## 18. Preview Runtime Changes
Для runtime changes показать:
- path;
- change type;
- text diff, если доступен;
- можно ли коммитить.

## 19. Commit Runtime Changes
Только в `Staging`.

Можно:
1. посмотреть diff;
2. выбрать changes;
3. подтвердить commit.

Commit идёт:
- в `develop`;
- или в текущий `release/<version>`.

После successful Push всех затронутых repository Commit + Push сохраняет
Baseline с exact SHA созданных commit для затронутых repository. SHA
незатронутого repository сохраняется, поэтому следующий Runtime Diff содержит
только невыбранные Runtime changes. Commit, Push или baseline persistence
failure, включая partial Push, не продвигает Baseline.

Предоставленный оператором commit message остаётся subject каждого созданного
commit. Handler добавляет одинаковые structured trailers в каждый затронутый
repository:

```text
RustServer-Operation: <operationId>
RustServer-Environment: staging
RustServer-Source: development | release/<version>
```

`operationId` берётся из detailed report текущей операции, поэтому Plugins и
Configs commit одной операции содержат один и тот же ID. Environment и Source
берутся из validated state. Git author identity берётся из service
configuration; операция не изменяет global или repository Git configuration.

## 20. Commit + Push
Для пользователя commit и push — одна операция.

Цель:
- не оставлять server-side repository в состоянии `ahead`.
- после full successful Push продвинуть Baseline до созданных commit SHA без
  дополнительного Apply Source.

Если push не удался:
- вернуть подробную ошибку;
- показать final local Git state;
- не ограничиваться просто `Failed`.

Baseline записывается только после successful Push всех repository, выбранных
для commit. Если его persistent save не удался, operation возвращает failure,
уже pushed remote commit не force-rollback'ится, а Baseline остаётся прежним.

## 21. Revert Runtime Changes
Вернуть managed Runtime files к Baseline. Без selection Revert восстанавливает
весь Managed Runtime. При передаче selection он восстанавливает только
выбранные current Runtime Diff entries; остальные Runtime changes остаются
нетронутыми. Пустая selection является ошибкой и никогда не означает Revert
all.

Для каждого выбранного файла Revert:
- удалить added;
- восстановить modified;
- восстановить deleted;
- вернуть detailed report.

Selection использует ту же пару `repository` (`plugins` или `configs`) и
logical managed Runtime `path`, что и Commit Runtime Changes. Она не содержит
duplicate entries, должна безопасно отображаться в Managed Runtime и обязана
существовать в текущем Runtime Diff. Источник bytes — только exact Git objects
из сохранённого Baseline, а не текущий Git HEAD.

Revert не меняет Git workspace, Baseline или `appliedAt`. При failure/cancel
rollback затрагивает только files в области текущей Revert operation.

Допускается при работающем Rust server. Revert меняет только managed Oxide Runtime files.

## 22. Switch Release
Flow:
```text
List Releases
  ↓
Select Release
  ↓
Preview
  ↓
Confirm
  ↓
Apply Source Change
```

Можно идти и вперёд, и назад.

## 23. Rollback Release
Отдельная backend-механика не нужна.

Rollback = `Switch Release` на более старую версию.

## 24. Server lifecycle independence
PluginManagement не проверяет состояние Rust как precondition для Git и
managed Oxide Runtime операций. При running Rust разрешены:
- Fetch, Git Sync, Git status, Release list;
- Runtime Diff, Preview, Baseline;
- Commit + Push;
- Apply, Revert;
- Source/Release switch и Rollback.

Успешная операция гарантирует только выполнение согласованного Git/Runtime
плана и его Baseline, но не проверяет результаты перезагрузки Oxide-плагинов.
Продолжают действовать exclusive mutation guard, Git safety и confirmation.

## 25. Get Operation Result
Любая изменяющая операция должна возвращать подробный результат.

Недостаточно:
```text
Failed
```

Нужно:
- operation id;
- operation type;
- status;
- current phase;
- source;
- repository;
- file path;
- human-readable reason;
- machine-readable error code;
- выполненные шаги;
- невыполненные шаги;
- rollback result;
- final Git state;
- final baseline state.

Report должен быть пригоден для копирования целиком.

Operation history сохраняется до restart в SQLite. Report регистрируется как
`inProgress` до начала mutation, обновляется по мере выполнения и завершается
как `succeeded`, `failed` или `interrupted`. Если persistent storage
недоступен, mutation не начинается; если не удаётся сохранить final report,
операция не сообщается как successful.

История читается отдельным compact списком, отсортированным от новых к старым.
Пагинация использует непрозрачный cursor пары `startedAt + operationId`, чтобы
новые операции между запросами не создавали дубликаты или смещение страницы.
Полный detailed report читается по operation ID.

## 26. Detailed Failure Report
Ошибка должна объяснять:
- на каком шаге;
- в каком repository;
- на каком файле;
- что выполнялось;
- что уже изменилось;
- был ли rollback;
- в каком состоянии система осталась.

Пример:
```text
Operation: Apply release/2.0
Status: Failed

Phase: Write managed files
Repository: Configs
File: oxide/config/Example.json

Reason:
Access denied while replacing target file.

Completed before failure:
- Plugins checkout: OK
- Configs checkout: OK
- 12 files written

Rollback:
- Runtime files restored: OK
- Baseline unchanged: OK
```

## 27. Create Release — TODO / Future
В будущем система может уметь создать новый Release.

Create Release означает:
- создать одинаковую `release/<version>` branch в Plugins;
- создать такую же branch в Configs.

Это не отдельный JSON release object.

Для первой версии operation можно не реализовывать.

## 28. UI Representation Guidance
Operations должны позволять UI показывать состояние визуально.

Complete:
```text
Plugins   ✓ release/2.0
Configs   ✓ release/2.0
          ↓
       Complete
```

Incomplete:
```text
Plugins   ✓ release/3.0
Configs   ✕
          ↓
       Incomplete
```

Source transition:
```text
release/1.0
    ↓
  12 files
    ↓
release/2.0
```

Предпочтение: схемы, иконки, counters, короткие labels вместо длинного текста.

---

# Итоговая схема

Read flow:
```text
Fetch Git
       ↓
Get Git Status
       ↓
Git Sync selected repository when required
       ↓
List Releases
       ↓
Select Target Source
       ↓
Preview
```

Write flow:
```text
Preview
  ↓
Confirm
  ↓
Apply Source Change
  ↓
Detailed Operation Report
```

Staging hotfix:
```text
Get Runtime Diff
      ↓
Select Changes
      ↓
Commit + Push
      ↓
Detailed Operation Report
```

Production:
```text
Get Runtime Diff
      ↓
Select Release
      ↓
Preview reset/apply
      ↓
Confirm
      ↓
Apply Release
      ↓
Detailed Operation Report
```

На текущем этапе зафиксировано **28 операций/правил поведения**.

Следующий документ — `api-contract.md`.
