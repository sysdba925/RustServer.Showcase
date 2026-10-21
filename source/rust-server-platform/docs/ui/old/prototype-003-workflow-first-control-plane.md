# Prototype #3 — Workflow-first Control Plane

## Основная идея

Этот вариант строится не вокруг вопроса:

> «Что сейчас находится на сервере?»

и не вокруг:

> «Какое точное состояние всех компонентов?»

а вокруг:

> **«Что я хочу сделать с сервером?»**

UI знает ограничения backend и сам выстраивает безопасную последовательность действий.

Пользователь не обязан помнить:

```text
нельзя обновлять работающий сервер

нужно:
Stop
→ Update
→ Start
```

Для него это одна операция:

```text
Update Oxide
```

А интерфейс показывает, **какие реальные шаги будут выполнены**.

---

# 1. Главный экран

```text
┌──────────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│  RUST SERVER PLATFORM                                         rust-01                         ● READY          │
├──────────────────────┬───────────────────────────────────────────────────────────────────────────────────────┤
│                      │                                                                                       │
│  ▣ Home              │  rust-01                                                                              │
│                      │  Ready · No active operations                                                         │
│  OPERATE             │                                                                                       │
│    Update            │  WHAT DO YOU WANT TO DO?                                                             │
│    Restart           │                                                                                       │
│    Maintenance       │  ┌──────────────────────┐  ┌──────────────────────┐  ┌──────────────────────┐         │
│                      │  │ ↑ UPDATE SOFTWARE    │  │ ↻ RESTART SERVER     │  │ ■ STOP SERVER        │         │
│  MANAGE              │  │                      │  │                      │  │                      │         │
│    Software          │  │ Rust and Oxide      │  │ Restart running      │  │ Stop Rust safely     │         │
│    Plugins           │  │ maintenance         │  │ server               │  │                      │         │
│    Configuration     │  │                      │  │                      │  │                      │         │
│    World             │  │ Continue →           │  │ Restart →            │  │ Stop →               │         │
│    Players           │  └──────────────────────┘  └──────────────────────┘  └──────────────────────┘         │
│                      │                                                                                       │
│  OBSERVE             │                                                                                       │
│    Operations        │  SERVER                                                                               │
│    Logs              │                                                                                       │
│                      │  ┌─────────────────────────────────────────────────────────────────────────────────┐  │
│                      │  │ ● READY                                                                         │  │
│                      │  │                                                                                 │  │
│                      │  │ Rust server is running and accepting connections.                              │  │
│                      │  │                                                                                 │  │
│                      │  │ Rust       25454815        Current                                             │  │
│                      │  │ Oxide      2.0.7726        Update available                                    │  │
│                      │  └─────────────────────────────────────────────────────────────────────────────────┘  │
│                      │                                                                                       │
│                      │  RECENT                                                                               │
│                      │                                                                                       │
│                      │  ✓ Server started                                             21:39                   │
│                      │  ✓ Oxide update completed                                     Yesterday               │
│                      │  ✓ Rust update completed                                      Yesterday               │
│                      │                                                                                       │
└──────────────────────┴───────────────────────────────────────────────────────────────────────────────────────┘
```

Главное отличие от Prototype #1:

```text
Prototype #1

SERVER
RUST
OXIDE
ACTIVITY
```

Prototype #3:

```text
WHAT DO YOU WANT TO DO?

Update
Restart
Maintenance
...

        ↓

Current state
```

То есть состояние системы остаётся видно, но **не является главным содержимым страницы**.

---

# 2. Пользователь выбирает Update

Нажимаем:

```text
[ UPDATE SOFTWARE ]
```

Переходим не сразу к выполнению, а к экрану подготовки операции.

```text
┌──────────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│  RUST SERVER PLATFORM                                         rust-01                         ● READY          │
├──────────────────────┬───────────────────────────────────────────────────────────────────────────────────────┤
│                      │                                                                                       │
│  Home                │  Update software                                                                      │
│                      │                                                                                       │
│  OPERATE             │  Keep your Rust server software current.                                             │
│  > Update            │                                                                                       │
│    Restart           │                                                                                       │
│    Maintenance       │  SOFTWARE                                                                             │
│                      │                                                                                       │
│  MANAGE              │  ┌─────────────────────────────────────────────────────────────────────────────────┐  │
│    Software          │  │ RUST                                                                            │  │
│    Plugins           │  │                                                                                 │  │
│    Configuration     │  │ Installed       25454815                                                        │  │
│    World             │  │ Available       25454815                                      ✓ Current         │  │
│                      │  │                                                                                 │  │
│  OBSERVE             │  │ No update required.                                                             │  │
│    Operations        │  └─────────────────────────────────────────────────────────────────────────────────┘  │
│    Logs              │                                                                                       │
│                      │  ┌─────────────────────────────────────────────────────────────────────────────────┐  │
│                      │  │ OXIDE                                                                           │  │
│                      │  │                                                                                 │  │
│                      │  │ Installed       2.0.7726                                                        │  │
│                      │  │ Running         2.0.7726                                                        │  │
│                      │  │ Available       2.0.7731                                    ↑ Update available   │  │
│                      │  │                                                                                 │  │
│                      │  │  ○ Do not update                                                              │  │
│                      │  │  ● Update to 2.0.7731                                                         │  │
│                      │  └─────────────────────────────────────────────────────────────────────────────────┘  │
│                      │                                                                                       │
│                      │                                                       [ Cancel ]  [ Continue → ]       │
│                      │                                                                                       │
└──────────────────────┴───────────────────────────────────────────────────────────────────────────────────────┘
```

На этом этапе UI ещё ничего не меняет.

Он просто определяет:

```text
Rust   → current
Oxide  → update required
```

---

# 3. Самая важная часть — Plan

После `Continue` интерфейс объясняет, **что именно произойдёт**.

```text
┌──────────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│  Update Oxide                                                                                               │
│                                                                                                              │
│  The server is currently running.                                                                           │
│  Updating Oxide requires the server to be stopped.                                                          │
│                                                                                                              │
│  EXECUTION PLAN                                                                                              │
│                                                                                                              │
│      ┌─────┐                                                                                                 │
│      │  1  │  Stop Rust server                                                                               │
│      └──┬──┘                                                                                                 │
│         │                                                                                                    │
│         ▼                                                                                                    │
│      ┌─────┐                                                                                                 │
│      │  2  │  Update Oxide                                                                                   │
│      └──┬──┘  2.0.7726 → 2.0.7731                                                                           │
│         │                                                                                                    │
│         ▼                                                                                                    │
│      ┌─────┐                                                                                                 │
│      │  3  │  Start Rust server                                                                              │
│      └──┬──┘                                                                                                 │
│         │                                                                                                    │
│         ▼                                                                                                    │
│      ┌─────┐                                                                                                 │
│      │  4  │  Wait until server is Ready                                                                     │
│      └─────┘                                                                                                 │
│                                                                                                              │
│  Server will be unavailable while this workflow is running.                                                 │
│                                                                                                              │
│                                                                     [ Back ]   [ Run update ]                │
└──────────────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

Здесь UI впервые превращает несколько backend-команд в **одну понятную человеческую операцию**.

Текущий backend фактически требует остановки сервера перед Rust/Oxide update и блокирует lifecycle во время выполняющегося update.

При этом сегодня compound endpoint `Stop → Update → Start` отсутствует, поэтому это именно направление UX/будущей orchestration-логики, а не уже существующая backend-функция.

---

# 4. Выполнение workflow

После подтверждения экран полностью меняется.

Главное сейчас — не версии и не dashboard.

Главное — **операция, которую пользователь запустил**.

```text
┌──────────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│  RUST SERVER PLATFORM                                      rust-01                       ◉ MAINTENANCE        │
├──────────────────────┬───────────────────────────────────────────────────────────────────────────────────────┤
│                      │                                                                                       │
│  Home                │  Update Oxide                                                                         │
│                      │                                                                                       │
│  OPERATE             │  Workflow running                                                                     │
│  > Update            │  Started 22:21:09                                                                     │
│    Restart           │                                                                                       │
│    Maintenance       │                                                                                       │
│                      │  ┌─────────────────────────────────────────────────────────────────────────────────┐  │
│  MANAGE              │  │                                                                                 │  │
│    Software          │  │   ✓   1. Stop server                                                            │  │
│    Plugins           │  │       Completed 22:21:11                                                       │  │
│    Configuration     │  │       │                                                                         │  │
│    World             │  │       │                                                                         │  │
│                      │  │       ▼                                                                         │  │
│  OBSERVE             │  │                                                                                 │  │
│    Operations        │  │   ●   2. Update Oxide                                                          │  │
│    Logs              │  │       Running                                                                  │  │
│                      │  │       2.0.7726 → 2.0.7731                                                      │  │
│                      │  │       │                                                                         │  │
│                      │  │       │                                                                         │  │
│                      │  │       ▼                                                                         │  │
│                      │  │                                                                                 │  │
│                      │  │   ○   3. Start server                                                          │  │
│                      │  │       Waiting                                                                   │  │
│                      │  │       │                                                                         │  │
│                      │  │       │                                                                         │  │
│                      │  │       ▼                                                                         │  │
│                      │  │                                                                                 │  │
│                      │  │   ○   4. Wait for Ready                                                        │  │
│                      │  │       Waiting                                                                   │  │
│                      │  │                                                                                 │  │
│                      │  └─────────────────────────────────────────────────────────────────────────────────┘  │
│                      │                                                                                       │
│                      │  CURRENT STEP                                                                         │
│                      │                                                                                       │
│                      │  Oxide Update                                                                         │
│                      │  ● Running                                                                            │
│                      │  Duration   00:02                                                                      │
│                      │                                                                                       │
└──────────────────────┴───────────────────────────────────────────────────────────────────────────────────────┘
```

Backend operation уже имеет реальные состояния:

```text
Pending
Running
Succeeded
Failed
```

поэтому второй шаг естественно отображает существующую operation model.

---

# 5. После завершения Update

Следующий шаг запускается автоматически.

```text
UPDATE OXIDE

✓ 1. Stop server

✓ 2. Update Oxide
    2.0.7726 → 2.0.7731
    Completed in 2.6 sec

● 3. Start server
    Starting...

○ 4. Wait for Ready
```

Но важно: `Start` ещё не означает, что всё закончено.

У backend есть промежуточное состояние:

```text
isRunning = true
isReady   = false
```

поэтому workflow продолжает ожидать readiness.

---

# 6. Starting — отдельный шаг

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│                                                                              │
│  ✓   Stop server                                                             │
│      Completed                                                               │
│      │                                                                       │
│      ▼                                                                       │
│                                                                              │
│  ✓   Update Oxide                                                            │
│      2.0.7726 → 2.0.7731                                                     │
│      │                                                                       │
│      ▼                                                                       │
│                                                                              │
│  ✓   Start server                                                            │
│      Process started                                                         │
│      │                                                                       │
│      ▼                                                                       │
│                                                                              │
│  ●   Wait for Ready                                                          │
│                                                                              │
│      Rust process is running.                                                │
│      Waiting for server initialization...                                    │
│                                                                              │
│      Running       Yes                                                       │
│      Ready         No                                                        │
│                                                                              │
└──────────────────────────────────────────────────────────────────────────────┘
```

Так состояние `Starting` перестаёт быть странным промежуточным статусом.

Оно становится естественной частью процесса.

---

# 7. Workflow completed

После `isReady = true`:

```text
┌──────────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│                                                                                                              │
│                                      ✓ UPDATE COMPLETED                                                      │
│                                                                                                              │
│                                        Oxide 2.0.7731                                                        │
│                                                                                                              │
│  ─────────────────────────────────────────────────────────────────────────────────────────────────────────   │
│                                                                                                              │
│      ✓  Server stopped                                                                                       │
│      ✓  Oxide updated                 2.0.7726 → 2.0.7731                                                   │
│      ✓  Server started                                                                                       │
│      ✓  Server ready                                                                                         │
│                                                                                                              │
│      Total duration                   01:14                                                                   │
│                                                                                                              │
│  ─────────────────────────────────────────────────────────────────────────────────────────────────────────   │
│                                                                                                              │
│      Server                           ● READY                                                                 │
│      Installed Oxide                  2.0.7731                                                               │
│      Running Oxide                    2.0.7731                                                               │
│      Available Oxide                  2.0.7731                                                               │
│                                                                                                              │
│                                                     [ View operation ]   [ Done ]                            │
│                                                                                                              │
└──────────────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

Это важный момент концепта:

пользователь получает не просто:

```text
Success
```

а подтверждение конечного состояния:

```text
Installed = new
Running   = new
Server    = Ready
```

---

# 8. Ошибка — тоже workflow, а не красный toast

Например Oxide update завершился `Failed`.

```text
┌──────────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│  Update Oxide                                                                    ⚠ ACTION REQUIRED          │
│                                                                                                              │
│  ✓   1. Stop server                                                                                          │
│      │                                                                                                       │
│      ▼                                                                                                       │
│                                                                                                              │
│  ✕   2. Update Oxide                                                                                         │
│      Failed                                                                                                  │
│                                                                                                              │
│      Exit code       1                                                                                       │
│      Error           Update process failed                                                                   │
│                                                                                                              │
│  ─────────────────────────────────────────────────────────────────────────────────────────────────────────   │
│                                                                                                              │
│  The workflow has stopped.                                                                                   │
│  The Rust server is currently stopped.                                                                       │
│                                                                                                              │
│  ○   3. Start server                      Not executed                                                       │
│  ○   4. Wait for Ready                    Not executed                                                       │
│                                                                                                              │
│                                                          [ Start server ]  [ Retry update ]                  │
└──────────────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

То есть UI не делает вид:

```text
Update failed ☹
```

и оставляет пользователя гадать, в каком состоянии теперь сервер.

Он говорит:

```text
что успело выполниться
где произошла ошибка
что не выполнялось
в каком состоянии система сейчас
что можно сделать дальше
```

---

# 9. Workflow может адаптироваться к текущему состоянию

Если сервер **уже остановлен**, план автоматически короче:

```text
UPDATE OXIDE

Current server state: STOPPED


EXECUTION PLAN

1   Update Oxide
    2.0.7726 → 2.0.7731

2   Start server

3   Wait for Ready


[ Run update ]
```

А если пользователь не хочет автоматически запускать сервер:

```text
After update

● Start server
○ Leave server stopped
```

тогда:

```text
1 Update Oxide
2 Finish
```

Таким образом workflow строится **из текущего состояния**, а не является жёстким скриптом.

---

# 10. Software всё равно существует как отдельный экран

Workflow-first не означает, что мы прячем техническую информацию.

В `Software` остаётся нормальный inspection screen:

```text
SOFTWARE

RUST
────────────────────────────────────────────────────

Installed       25454815
Available       25454815                 CURRENT

Running
  Protocol      2633.288.1
  Changeset     165217
  Unity         6000.3.15x1-11
  Branch        release


OXIDE
────────────────────────────────────────────────────

Installed       2.0.7726
Running         2.0.7726
Available       2.0.7731                 UPDATE AVAILABLE


                                        [ Update → ]
```

То есть:

```text
Home
    = задачи

Software
    = данные
```

Это очень важное разделение.

---

# 11. Operations

Каждый workflow постепенно может стать полноценной operation history.

```text
OPERATIONS

Today
────────────────────────────────────────────────────────────────────

✓ 22:21   Update Oxide
          2.0.7726 → 2.0.7731
          1m 14s

✓ 18:42   Restart server
          48s

✕ 14:07   Update Rust
          Failed during update
          Server left stopped


Yesterday
────────────────────────────────────────────────────────────────────

✓ 21:12   Update Rust
          25454102 → 25454815
          2m 08s
```

Сегодня operation store ещё находится в памяти и полноценной persistent history нет.

Но UI уже может быть спроектирован под модель, в которую она позднее естественно добавится.

---

# 12. Будущие workflow

Главная сила этой концепции проявляется позже.

Сейчас:

```text
Update Rust
Update Oxide
Restart
```

В будущем:

```text
UPDATE SOFTWARE

Stop
→ Backup
→ Update Rust
→ Update Oxide
→ Update plugins
→ Start
→ Verify Ready
```

или:

```text
WIPE SERVER

Notify players
→ Stop server
→ Backup world
→ Archive map
→ Generate new map
→ Apply wipe configuration
→ Start server
→ Verify Ready
```

или:

```text
RESTORE BACKUP

Stop
→ Backup current state
→ Restore selected backup
→ Start
→ Verify Ready
```

или:

```text
PLUGIN UPDATE

Check dependencies
→ Stop if required
→ Update plugin
→ Reload / Start
→ Verify plugin
```

Поэтому `Automation`, `Backups`, `World` и другие будущие области платформы хорошо укладываются именно в workflow-oriented модель. `Plugins` уже имеет опубликованный backend contract для Environment, Source, Development, Releases, update packages и runtime changes; его UI должен опираться на эти use cases, а не на этот иллюстративный lifecycle-oriented пример. `Automation`, `Backups` и другие перечисленные области по-прежнему являются возможным развитием control plane, когда для них появятся backend capabilities.

---

# 13. Navigation

Для Prototype #3 я бы использовал навигацию такого типа:

```text
RUST SERVER PLATFORM

rust-01
────────────────────

HOME

OPERATE
  Update
  Restart
  Maintenance

MANAGE
  Software
  Plugins
  Players
  World
  Configuration
  Economy

AUTOMATE
  Schedules
  Workflows

OBSERVE
  Operations
  Logs
```

То есть меню тоже строится вокруг глаголов:

```text
OPERATE
MANAGE
AUTOMATE
OBSERVE
```

а не просто вокруг технических сущностей.

---

# 14. Visual language

Этот вариант я бы сделал **светлым или neutral-dark**, но не настолько инженерным, как Prototype #2.

Не:

```text
enterprise forms
```

и не:

```text
game hosting panel
```

Скорее стиль:

```text
GitHub Actions
+
Vercel deployments
+
modern cloud control plane
```

Основные визуальные элементы:

```text
✓ completed
● active
○ waiting
✕ failed
│
▼
```

Много вертикальных последовательностей:

```text
✓ Stop
│
▼
● Update
│
▼
○ Start
│
▼
○ Verify
```

Небольшое количество цветов.

Цвет кодирует только состояние:

```text
neutral     waiting
blue        running
green       succeeded
red         failed
amber       requires attention
```

Основная информация должна читаться даже вообще без цвета.

---

# 15. Главная мысль Prototype #3

```text
Prototype #1
────────────────────────────

"Вот твой сервер."

       ↓

Server
Rust
Oxide
Activity
```

```text
Prototype #2
────────────────────────────

"Вот точное состояние системы."

       ↓

Runtime state
Software state
Operations
Commands
```

```text
Prototype #3
────────────────────────────

"Что ты хочешь сделать?"

       ↓

Goal
  │
  ▼
Plan
  │
  ▼
Execute
  │
  ▼
Verify
  │
  ▼
Result
```

Его базовая модель:

```text
              USER INTENT

                  │
                  ▼

        ┌───────────────────┐
        │     WORKFLOW      │
        └─────────┬─────────┘
                  │
         ┌────────┼────────┐
         ▼        ▼        ▼

       Stop     Update    Start

         │        │        │
         └────────┼────────┘
                  ▼

               VERIFY

                  │
                  ▼

             SERVER READY
```

То есть UI становится **прослойкой между человеческим намерением и низкоуровневыми backend operations**.

Пользователь говорит:

```text
Update Oxide
```

а платформа понимает:

```text
Check current state
→ Stop if required
→ Run OxideUpdate
→ Observe operation
→ Start
→ Wait for Ready
→ Verify versions
→ Finish
```

В этом и состоит концептуальная разница Prototype #3.
