# RustServer.Plugins — Requirements Source of Truth

**Status:** Working specification  
**Purpose:** единый источник истины для поведения Plugins management в RustServer.  
**Scope:** Staging / Production, Development / Release, Git, baseline, runtime diff, apply/revert/commit, release navigation.  
**Important:** документ описывает требуемое поведение системы. Он не фиксирует конкретные API endpoints.

---

## 1. Основная модель

1. Система имеет два Environment: **Staging** и **Production**.

2. В **Staging** доступны два Source:
   - `Development`
   - `Release`

3. В **Production** доступен только Source:
   - `Release`

4. `Development` в Production не показывается и не может быть выбран.

5. Переключение `Staging <-> Production` разрешено в обе стороны без искусственных ограничений.

6. Environment и Source — разные понятия:
   - Environment = где мы работаем: `Staging` или `Production`
   - Source = откуда берём файлы: `Development` или конкретный `Release`

7. Допустимые комбинации:
   - `Staging + Development`
   - `Staging + Release`
   - `Production + Release`

8. Недопустимая комбинация:
   - `Production + Development`

9. UI всегда должен явно показывать текущий Environment.

10. UI всегда должен явно показывать текущий выбранный Source.

---

## 2. Git repositories

11. Всегда участвуют два Git repository:
   - Plugins
   - Configs

12. Локальные Git repository лежат отдельно от папки Rust server.

13. `Development` использует ветку `develop` в обоих repository.

14. `Release` использует ветки вида:
   - `release/1.0`
   - `release/2.0`
   - `release/3.0`

15. Другие ветки считаются неподдерживаемым состоянием для управления через Web UI.

16. Номер Release должен совпадать в Plugins и Configs.

17. Например, допустимо:
   - Plugins: `release/2.0`
   - Configs: `release/2.0`

18. Если в одном repository есть `release/2.0`, а в другом нет, это blocking state.

19. При несовпадении Release система ничего автоматически не исправляет.

20. При несовпадении Release UI должен явно показать проблему.

21. Исправление такого Git state выполняется вручную внешними Git-инструментами.

### GitHub runtime credential (approved contract)

- The Plugins service uses its **own** GitHub credential, separate from Updater's: host source `/etc/rustserver/plugins-github-token`, supplied as a protected systemd `LoadCredential` copy.
- The service-side credential file location is obtained from `RUSTSERVER_PLUGINS_GITHUB_TOKEN_FILE`; only the file path (never the credential value) is configured.
- The credential is intended for read/write Git operations **only** on `sysdba925/RustServer.PluginFiles` and `sysdba925/RustServer.ConfigFiles`. Updater retains `/etc/rustserver/updater-github-token` for release read access.
- Bootstrap owns the independent root-owned `0600` source file and systemd wiring, not Git clone/fetch/push. Both files may initially contain the same token value; later rotation must be independent.
- No token values in settings, Git remote URLs/config, logs, errors, or process command lines. Missing/empty/unreadable credentials produce non-secret diagnostics. Do not make local-only reads or startup require GitHub access.
- Authenticated GitHub HTTPS `Fetch` and `Push` invoke Git through a short-lived askpass helper which reads only the protected systemd credential copy. The token must not enter arguments, environment values, Git configuration or logs. Redirects are prohibited and unexpected HTTPS origins are rejected.
- Existing local/SSH remotes continue to use their previous transport until the explicit migration. Git Workspace Initialization, Installer/Bootstrap credential creation and systemd wiring are separate stages.

---

## 2.1. Первый запуск и права владения Git workspace

- Plugins самостоятельно клонирует отсутствующие `RustServer.ConfigFiles`
  и `RustServer.PluginFiles` из HTTPS на ветку `develop`.
- Вложенная Development-структура `oxide/plugins` и отдельные
  Production-каталоги под `/var/lib/rustserver/plugins/workspaces`
  поддерживаются одинаково; заранее созданная пустая директория допустима.
- Existing repository, local commits, branches, uncommitted changes and
  baseline must be preserved on startup. No reset, merge or hidden fetch.
- Non-empty foreign directories or mismatched origin block initialization
  with diagnostics; cloning must not overwrite operator files.
- Bootstrap supplies path/ownership and independent `0600` token; Plugins
  owns cloning and Git changes. The old Development Bootstrap workspace
  sync steps remain temporarily until the migration stages.
- Startup initialization is enabled through the deployment systemd
  configuration, but a missing GitHub credential must not prevent local
  read-only endpoints from serving their current diagnostic state.

---

## 3. Получение актуального Git state

22. Система не должна постоянно автоматически опрашивать GitHub.

23. В UI должна быть отдельная команда **Fetch**, которая обновляет только
    известную информацию о remote Git.

24. Эта команда обновляет информацию из remote Git для обоих repository.

25. После Fetch система должна видеть новые remote release branches.

26. После Fetch система должна видеть актуальное remote состояние `develop`.

27. Отдельная команда **Git Sync** приводит выбранную поддерживаемую local
    branch к её `origin` branch или публикует локальные commits. Перед Apply,
    Commit Runtime и другими Git mutation система проверяет свежий remote
    state и не делает неявный Sync.

28. UI должен показывать, если локальная ветка отличается от remote.

29. Git Sync выбирает только документированное действие по проверенному
    отношению local/remote: `equal` — no-op, `behind` — fast-forward,
    `ahead` — push, `diverged` — regular merge and push. Он никогда не
    использует rebase, reset или force push.

30. Dirty workspace, неподдерживаемая/detached branch, отсутствующий или
    неверный upstream, merge conflict и другой unsafe Git state блокируют
    Sync с понятной диагностикой. Конфликт или неудачное recovery оператор
    исправляет внешним Git-инструментом.

---

## 4. Release list и навигация

31. UI должен показывать несколько доступных Release, а не только текущий.

32. Release list формируется по release branches из Git.

33. Пример списка:
   - `release/2.0`
   - `release/1.0`
   - `release/0.9`

34. Текущий Release сервера должен быть явно отмечен.

35. Новый Release, найденный после Fetch, должен появляться в списке.

36. Старые Release должны оставаться доступными для rollback.

37. Необязательно загружать всю историю сразу.

38. Старые Release можно загружать страницами или через `Load more`.

39. Пользователь может свободно переходить между доступными Release вперёд и назад.

40. Переход на старый Release является обычным rollback через тот же механизм применения Source к серверу.

---

## 5. Создание Release

41. Новый Release представляет собой новую release branch с новым номером.

42. Например:
   - текущий `release/1.0`
   - новый `release/2.0`

43. Новый Release должен существовать в обоих repository с одинаковым номером.

44. Создание Release не должно автоматически менять файлы Rust server.

45. После создания Release он появляется как доступный Source.

46. Чтобы перевести сервер на новый Release, пользователь отдельно выбирает его и применяет к серверу.

---

## 6. Hotfix внутри Release

47. Hotfix не создаёт новую release branch.

48. Если текущий Release — `release/1.0`, hotfix коммитится прямо в `release/1.0`.

49. Внутри одной release branch может быть несколько commit.

50. Имя Release при hotfix не меняется автоматически.

51. Например:
   - `release/1.0` commit A
   - `release/1.0` commit B — hotfix
   - `release/1.0` commit C — ещё hotfix

52. Merge/cherry-pick Release -> Development не является обязанностью RustServer.Plugins.

53. Merge/cherry-pick выполняется внешними Git-инструментами.

---

## 7. Baseline

54. Система должна хранить **baseline** последнего успешно materialized
    состояния Rust server: successful Apply или successful Commit + Push
    выбранных Runtime changes.

55. Baseline должен храниться в понятном service-owned файле рядом с корнем рабочего storage, а не глубоко во внутренних каталогах.

56. Baseline должен содержать как минимум:
   - Environment
   - Source type
   - Release name, если Source = Release
   - Plugins commit SHA
   - Configs commit SHA
   - время применения

57. Baseline описывает точный Git reference, с которым сравниваются Runtime
    files. После Apply он содержит применённый Source; после полностью
    успешного Commit + Push он продвигается до созданных commit SHA только в
    затронутых repository.

58. Baseline не означает, что runtime-файлы после запуска сервера обязательно остались неизменными.

59. Baseline должен переживать restart RustServer.Plugins.

60. Все persistent state системы должны переживать restart сервиса.

---

## 8. Diff

61. Система должна уметь сравнивать текущие файлы Rust server с baseline.

62. Diff должен показывать:
   - modified
   - added
   - deleted

63. Просматривать diff можно при запущенном Rust server.

64. Просмотр diff ничего не изменяет.

65. Система должна показывать отдельно:
   - baseline сервера
   - выбранный Git Source
   - текущий HEAD выбранной ветки

66. Если baseline Release указывает на старый SHA, а release branch уже ушла вперёд, UI должен это явно показать.

67. Пример:
   - Server baseline: `release/1.0 @ AAA`
   - Current branch HEAD: `release/1.0 @ BBB`
   - Status: Release has newer Git changes

67d. Runtime Diff and all Runtime-targeted managed-file operations must
exclude files ignored by the local source Git workspace when those files
are not tracked in Git. Ignored Runtime files must not be listed as changes,
selected for Commit + Push/Revert, or removed as obsolete during Apply.
The source of truth for this selection is Git ignore resolution, including
nested rules and negations. Tracked Git files remain managed. If the ignore
rules cannot be evaluated, fail closed rather than assuming every Runtime
file is managed.

67a. Клиент может подписаться на уведомления о том, что managed Runtime files
    **могли** измениться. Уведомление не содержит diff, не является
    persistent state и не заменяет повторное чтение Runtime Diff.

67b. Изменения unmanaged файлов не должны создавать Runtime notification.
    Несколько близких filesystem events могут быть объединены в одно
    уведомление; потерянное filesystem notification не влияет на корректность
    следующего Runtime Diff.

67c. Для отдельного changed managed file система должна уметь по запросу
    прочитать обе стороны Runtime Diff: содержимое из exact Baseline commit и
    текущее Runtime содержимое. Binary или неподдерживаемое text содержимое
    не должно интерпретироваться как text preview.

---

## 9. Git -> Server

68. Операция Git -> Server не должна называться двусторонним `Sync`.

69. Название должно явно показывать направление, например:
   - `Apply to Server`
   - `Update Server`

70. Перед Apply система показывает preview diff.

71. Пользователь должен подтвердить Apply.

72. Apply копирует состояние выбранного Git Source в управляемые файлы Rust server.

73. Apply должен приводить сервер к точному состоянию выбранного Source для управляемых файлов.

74. После успешного Apply baseline обновляется.

75. После Apply baseline должен содержать новые Plugins SHA и Configs SHA.

76. При переходе на другой Release baseline меняет Release identity.

77. Например:
   - было `release/1.0`
   - применили `release/2.0`
   - baseline становится `release/2.0`

78. Rollback выполняется тем же действием:
   - выбрать старый Release
   - посмотреть diff
   - подтвердить
   - Apply to Server

---

## 10. Server -> Git в Staging

79. В Staging разрешено забирать runtime changes обратно в Git.

80. В `Staging + Development` runtime changes можно коммитить в `develop`.

81. В `Staging + Release` runtime changes можно коммитить прямо в текущую release branch.

82. Перед commit пользователь должен видеть diff.

83. Пользователь должен иметь возможность выбрать изменения, которые попадут в commit.

84. Commit является явным действием и требует confirmation.

85. Commit в Release используется для hotfix.

86. После успешного commit Git HEAD меняется.

87. Baseline не должен продвигаться до завершения Commit + Push всех
    затронутых repository и успешного persistent save. Failed или partially
    successful Commit + Push не меняет Baseline.

88. После successful Commit + Push Baseline использует новые SHA созданных
    commit; дополнительный Apply Source не требуется. Selected Runtime changes
    больше не входят в Runtime Diff, а невыбранные Runtime changes остаются в
    нём видимыми.

88a. Commit Runtime сохраняет предоставленный оператором текст commit message
     как subject commit и не заменяет его общим service message.

88b. Каждый Commit Runtime commit должен содержать fixed structured trailers:
     `RustServer-Operation`, `RustServer-Environment` и
     `RustServer-Source`.

88c. `RustServer-Operation` содержит ID detailed operation report; если
     затронуты Plugins и Configs, оба commit используют один ID операции.

88d. `RustServer-Environment` и `RustServer-Source` описывают validated
     Environment и Source, для которых выполняется commit.

88e. Identity Commit Runtime commit берётся из конфигурации сервиса; операция
     не изменяет global или repository Git configuration.

---

## 11. Production restrictions

89. В Production нельзя коммитить runtime changes в Git.

90. В Production нельзя коммитить ни в `develop`, ни в release branch.

91. В Production runtime diff можно просматривать.

92. В Production можно выбрать другой Release.

93. В Production можно применить выбранный Release к серверу.

94. При Apply Release в Production runtime changes, которые отличаются от выбранного Release, будут потеряны.

95. Перед таким Apply UI обязан показать diff.

96. Перед таким Apply требуется confirmation.

97. Production работает по правилу:
   - посмотреть изменения
   - выбрать Release
   - подтвердить
   - привести сервер к точному состоянию Release

98. Если runtime changes нужно сохранить как hotfix, пользователь должен перейти в Staging и выполнить commit там.

---

## 12. Server lifecycle и безопасность операций

99. Просматривать status и diff можно при работающем Rust server.

100. Все операции PluginManagement над Git и управляемыми файлами Oxide допускаются при работающем Rust server: Fetch, Git Sync, Commit + Push, Apply, Revert, Source/Release switch и rollback.

101. Сервис изменяет только managed Oxide Runtime files, не управляет процессом Rust и не определяет, будет ли плагин перезагружен или как повлияет новая конфигурация.

102. Перед PluginManagement mutation **не требуется** проверка stopped/running состояния через RustServer.Control; отсутствие статуса сервера не блокирует операцию.

103. Exclusive mutation guard продолжает предотвращать пересечение операций над Git, Baseline и Runtime; сохраняются проверки Git, source, selection и файловой системы.

104. Сервер не останавливается и не перезапускается автоматически; оператор принимает эффекты изменения файлов плагинов на работающем Rust.

105. UI должен ясно показывать момент успешного завершения операции.

106. Успех Apply/Commit означает завершение файловой/Git операции и обновление Baseline, но не подтверждает фактическую перезагрузку/исправность отдельных плагинов.

---

## 13. Confirmation UX

107. Все действия с заметным риском требуют confirmation.

108. Confirmation нужен как минимум для:
   - Apply to Server
   - rollback
   - перехода на другой Release
   - revert runtime changes
   - commit runtime changes
   - действий, которые уничтожат текущий runtime diff

109. Confirmation должен показывать, что именно изменится.

110. Для Production confirmation должен особенно явно предупреждать о потере runtime changes.

111. Обычный просмотр status/diff confirmation не требует.

---

## 14. Development и Release — общая механика

112. Development и Release должны использовать максимально одинаковую механику.

113. В обоих случаях есть:
   - Git Source
   - Git HEAD
   - server baseline
   - runtime files
   - diff
   - Apply to Server

114. Главное отличие Development:
   - branch всегда `develop`

115. Главное отличие Release:
   - branch всегда `release/<version>`

116. В Staging оба Source могут принимать runtime commit.

117. В Production доступен только Release и runtime commit запрещён.

118. Backend не должен реализовывать две полностью независимые системы для Development и Release, если одна общая модель Source покрывает оба случая.

---

## 15. UI state и понятность

119. UI должен явно разделять:
   - Environment
   - Source
   - server baseline
   - Git HEAD
   - runtime diff

120. Пользователь не должен гадать, что сейчас является источником файлов.

121. Пользователь не должен гадать, какой Release реально применён на Rust server.

122. Пользователь не должен гадать, есть ли более новый commit в текущей branch.

123. Пользователь должен видеть направление каждого изменяющего действия:
   - Git -> Server
   - Server -> Git

124. В Production UI не должен показывать Development controls.

125. В Staging UI должен давать полный доступ к Development и Release workflow.

126. Для обычного управления Plugins пользователь не должен пользоваться SSH.

---

## 16. Что система намеренно НЕ делает

127. Система не выполняет недокументированное или destructive repair Git
     workspace, не переключает branch молча и не исправляет вручную
     сломанные/upstream-mismatched branches.

128. Git Sync может безопасно обработать только известное `diverged`
     отношением regular merge с последующим push; конфликт, failure abort или
     любое состояние вне документированных условий требуют ручного recovery.

129. Система не делает merge Release -> Development.

130. Система не создаёт hotfix branch для каждого hotfix.

131. Система не коммитит runtime changes из Production.

132. Система не применяет новый Release автоматически после его обнаружения или создания.

133. Система не должна делать background polling GitHub с высокой частотой.

---

## Краткая модель

```text
Environment
├── Staging
│   ├── Development -> develop
│   └── Release     -> release/<version>
│
└── Production
    └── Release     -> release/<version>
```

Для каждого Source:

```text
Git Source / HEAD
       |
       | compare
       v
Server Baseline
       |
       | compare
       v
Rust Runtime Files
```

Основные направления:

```text
Git -> Server
Review diff -> Confirm -> Apply -> Update baseline
```

```text
Server -> Git
Staging only -> Review diff -> Select changes -> Commit
```

Production:

```text
Review diff -> Select Release -> Confirm reset/apply -> Apply Release
No runtime commit
```

---

## Итог

На текущий момент зафиксировано **141 требование**.

Этот документ является текущим source of truth. Если решение меняется в обсуждении, сначала меняется это требование, а уже затем реализация.
