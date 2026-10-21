# Прототип UI №2: Control Room

Статус: исходный UX-макет. Этот документ сохраняет конкретную реализацию
второго варианта без упрощения его визуальных состояний.

Вариант №2 — не «ещё один dashboard», а операторский control room. Вариант
№1 — спокойная модульная админка, а здесь главным объектом становятся
состояние сервера и поток операций, а не карточки Rust и Oxide.

## Основной экран

```text
┌───────────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│  RUST CONTROL                                      rust-01                           ● READY      22:18:41     │
├────────────────────┬───────────────────────────────────────────────────────────────┬──────────────────────────┤
│                    │                                                               │                          │
│  OVERVIEW          │  rust-01                                                      │  QUICK ACTIONS           │
│                    │                                                               │                          │
│  Server            │  ● READY                                                      │  [ Restart server ]      │
│  Software          │  Rust Dedicated Server                                        │  [ Stop server    ]      │
│  Plugins           │                                                               │                          │
│  Players           │  ┌─────────────────────────────────────────────────────────┐  │  ----------------------  │
│  World             │  │                                                         │  │                          │
│  Configuration     │  │       SERVER HEALTH / STATE STRIP                       │  │  MAINTENANCE             │
│  Economy           │  │                                                         │  │                          │
│  Automation        │  │  PROCESS       READY        SOFTWARE       OPERATIONS    │  │  Rust Update            │
│                    │  │    ●             ●              ●              ●         │  │  Up to date             │
│  ----------------  │  │  Running       Ready         Current         Idle        │  │                          │
│                    │  │                                                         │  │  Oxide Update           │
│  Operations        │  └─────────────────────────────────────────────────────────┘  │  Up to date             │
│  Logs              │                                                               │                          │
│  Console           │                                                               │  [ Check for updates ]   │
│                    │  SOFTWARE STATE                                                │                          │
│                    │                                                               │  ----------------------  │
│                    │  RUST                                                         │  SERVER INFO             │
│                    │  Installed       25454815                                      │                          │
│                    │  Available       25454815                         CURRENT      │  Branch       public     │
│                    │  Running         release / 165217                              │  Environment  production │
│                    │  Protocol        2633.288.1                                    │  Host         rust-01    │
│                    │  Unity           6000.3.15x1-11                                │                          │
│                    │                                                               │                          │
│                    │  ───────────────────────────────────────────────────────────   │                          │
│                    │                                                               │                          │
│                    │  OXIDE                                                        │                          │
│                    │  Installed       2.0.7726                                      │                          │
│                    │  Available       2.0.7726                         CURRENT      │                          │
│                    │  Running         2.0.7726                                      │                          │
│                    │                                                               │                          │
├────────────────────┼───────────────────────────────────────────────────────────────┴──────────────────────────┤
│                    │                                                                                          │
│                    │  ACTIVITY                                                                                │
│                    │                                                                                          │
│                    │  22:14:37   ✓  Oxide update succeeded                         2.6 sec                    │
│                    │      │                                                                                   │
│                    │  22:14:34   ●  Oxide update started                                                      │
│                    │      │                                                                                   │
│                    │  22:13:52   ✓  Server stopped                                                              │
│                    │      │                                                                                   │
│                    │  21:58:11   ✓  Server started                                                              │
│                    │                                                                                          │
└────────────────────┴──────────────────────────────────────────────────────────────────────────────────────────┘
```

## Отличие от варианта №1

### 1. Нет крупных карточек Rust и Oxide

В первом варианте:

```text
[ SERVER ]

[ RUST ] [ OXIDE ]

[ ACTIVITY ]
```

Во втором:

```text
SERVER STATE
────────────

SOFTWARE STATE
Rust
...
────────────
Oxide
...

ACTIVITY
```

Это ближе к GitLab, Kubernetes dashboard или cloud control plane, чем к
игровой панели.

### 2. Постоянная operational-панель справа

Она не меняет основную информацию, а отвечает на вопрос: «Что я могу сделать
прямо сейчас?»

```text
┌──────────────────────────┐
│ QUICK ACTIONS            │
│                          │
│ [ Restart server ]       │
│ [ Stop server    ]       │
│                          │
│ -----------------------  │
│                          │
│ MAINTENANCE              │
│                          │
│ Rust       Up to date    │
│ Oxide      Up to date    │
└──────────────────────────┘
```

Центр является read-model, правая часть — commands. Это напрямую выражает
разделение backend:

```text
RustServer.Versions
        ↓
     observe

RustServer.Control
        ↓
      mutate
```

```text
                    UI

        READ                  COMMAND
         │                       │
         ▼                       ▼

  ┌──────────────┐       ┌──────────────┐
  │ center area  │       │ right panel  │
  │              │       │              │
  │ state        │       │ actions      │
  │ versions     │       │ updates      │
  │ activity     │       │ lifecycle    │
  └──────────────┘       └──────────────┘
```

## Активная операция

При запуске Oxide Update интерфейс превращается в операционную консоль:
operation становится центром экрана, потому что в данный момент она является
главным состоянием системы. Update имеет отдельные состояния `Pending`,
`Running`, `Succeeded`, `Failed`, блокирует другие mutating operations и
lifecycle.

```text
┌───────────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│  RUST CONTROL                                      rust-01                       ◉ MAINTENANCE     22:21:12     │
├────────────────────┬───────────────────────────────────────────────────────────────┬──────────────────────────┤
│                    │                                                               │                          │
│  Overview          │  ACTIVE OPERATION                                             │  SERVER CONTROLS         │
│  Server            │                                                               │                          │
│  Software          │  OXIDE UPDATE                                                 │  Start            —      │
│  Plugins           │                                                               │  Stop             —      │
│                    │  ● Running                                                    │  Restart          —      │
│  Players           │                                                               │                          │
│  World             │  Started       22:21:09                                       │  Locked by operation    │
│                    │  Duration      00:03                                           │                          │
│  Configuration     │                                                               │                          │
│  Economy           │  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━░░░░░░░░░░         │  ----------------------  │
│  Automation        │                                                               │                          │
│                    │                                                               │  UPDATES                 │
│  Operations        │                                                               │                          │
│  Logs              │  EXECUTION                                                     │  Rust             —      │
│                    │                                                               │  Oxide       Running      │
│                    │  Pending                                        ✓             │                          │
│                    │     │                                                         │                          │
│                    │     ▼                                                         │                          │
│                    │  Running                                        ●             │                          │
│                    │     │                                                         │                          │
│                    │     ▼                                                         │                          │
│                    │  Succeeded / Failed                              ○             │                          │
│                    │                                                               │                          │
├────────────────────┼───────────────────────────────────────────────────────────────┴──────────────────────────┤
│                    │                                                                                          │
│                    │  ACTIVITY                                                                                │
│                    │                                                                                          │
│                    │  22:21:09  ●  Oxide update started                                                       │
│                    │  22:20:42  ✓  Server stopped                                                             │
│                    │                                                                                          │
└────────────────────┴──────────────────────────────────────────────────────────────────────────────────────────┘
```

## Сервер остановлен

```text
┌──────────────────────────────────────────────────────────────────────────────────┐
│ rust-01                                                   ○ STOPPED              │
│                                                                                  │
│ SERVER STATE                                                                     │
│                                                                                  │
│ Process          Stopped                                                        │
│ Ready            No                                                             │
│                                                                                  │
│ Last running software                                                           │
│ Rust             25454815                                                       │
│ Oxide            2.0.7726                                                       │
│                                                                                  │
│                                                         [ Start server ]         │
└──────────────────────────────────────────────────────────────────────────────────┘
```

В этом состоянии maintenance-команды становятся активными:

```text
RIGHT PANEL

MAINTENANCE

Rust
Installed       25454815
Available       25455102

[ Update Rust ]


Oxide
Installed       2.0.7726
Available       2.0.7731

[ Update Oxide ]
```

## Модель ресурса и навигация

Главную страницу можно называть по серверу, а не `Overview`:

```text
RUST CONTROL

Servers
   └─ rust-01
```

После выбора сервера:

```text
rust-01

Summary
Runtime
Software
Players
World
Configuration
Operations
Logs
```

Это сразу предполагает будущую multi-server модель, хотя сейчас backend
поддерживает один сервер:

```text
SERVERS

● rust-eu-01
● rust-eu-02
○ rust-test
● rust-event
```

Навигация infrastructure-ориентирована и выражает работу с выбранным managed
resource, а не с «сайтом с разделами»:

```text
rust-01

  Summary
  Runtime
  Software
  Players
  World

MANAGE
  Configuration
  Economy
  Automation

OBSERVE
  Operations
  Logs
```

## Визуальный язык

```text
Dark graphite
почти нет floating cards
тонкие separators
моноширинные числа/версии
очень плотная информация
status-color используется только для состояний
```

Например:

```text
RUST

installed       25454815
available       25454815    CURRENT
protocol        2633.288.1
changeset       165217
branch          release
```

По духу это Datadog, Grafana, Kubernetes dashboard, GitLab admin или
AWS/Azure control panel, а не game server hosting panel.

## Сравнение прототипов

```text
PROTOTYPE 1                         PROTOTYPE 2

PRODUCT DASHBOARD                   CONTROL ROOM

┌───────────────┐                   STATE
│ SERVER        │                   ───────────────────
│   READY       │                   process       running
└───────────────┘                   ready         yes

┌───────┐ ┌───────┐                SOFTWARE
│ RUST  │ │ OXIDE │                ───────────────────
└───────┘ └───────┘                Rust
                                    installed ...
┌───────────────┐                   available ...
│ ACTIVITY      │
└───────────────┘                   Oxide
                                    installed ...
                                    available ...

easy / visual                       dense / operational
cards                               rows + inspectors
product-like                        infrastructure-like
```

Первый вариант более доступный и продуктовый. Второй — более взрослый server
control plane, где интерфейс выглядит как инструмент администратора, а не
панель игрового хостинга.
