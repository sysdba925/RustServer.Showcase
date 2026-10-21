# Prototype #1 — Overview

Status: selected visual direction and UX reference. This document describes
the current read-oriented Overview composition; the implementation contracts
are the [Overview](../screens/overview.md), [Server](../screens/server.md), and
[World](../screens/world.md) specifications.

## Visual concept

The shell is a calm, product-oriented control plane with a persistent header
and sidebar. Its implemented primary navigation is Overview, Server, and World.

```text
┌────────────────────────────────────────────────────────────────────────────────────┐
│ RUST SERVER PLATFORM                                                               │
├──────────────────┬─────────────────────────────────────────────────────────────────┤
│ ▣ Overview       │ Overview                                                        │
│   Server         │                                                                 │
│   World          │ SERVER                                                          │
│                  │ ┌─────────────────────────────────────────────────────────────┐ │
│                  │ │ SERVER STATUS                                      READY    │ │
│                  │ │ Rust server is running and ready.                           │ │
│                  │ │                                             [ Manage server ]│ │
│                  │ └─────────────────────────────────────────────────────────────┘ │
│                  │                                                                 │
│                  │ SOFTWARE                                                        │
│                  │ ┌───────────────────────┐ ┌───────────────────────────────────┐ │
│                  │ │ RUST                  │ │ OXIDE                             │ │
│                  │ │ Installed / Available │ │ Installed / Running / Available   │ │
│                  │ │ update CTA            │ │ update CTA                        │ │
│                  │ └───────────────────────┘ └───────────────────────────────────┘ │
│                  │                                                                 │
│                  │ ACTIVITY: current or recently observed operation                │
└──────────────────┴─────────────────────────────────────────────────────────────────┘
```

Overview is a read model and shortcut surface. Start, Stop, Restart, detailed
Running/Ready values, lifecycle errors, and startup milestones are on Server,
not duplicated on Overview.

## Starting summary

When the source lifecycle state is Starting and startup tracking has an active
session, Overview presents only a compact source-derived step count:

```text
STARTING
Rust process is running. Waiting for server readiness.
Startup: 5 / 9 milestones
                                             [ Manage server ]
```

`5 / 9` counts source states that are not `pending` (`reached` or `skipped`).
It is not a percentage or time estimate. The milestone names and completion
detail remain on Server. When Ready or Stopped, Overview does not retain a
completed milestone list.

## Server and World boundaries

Server is the detailed lifecycle destination. It presents Runtime state and the
ordered compact-width startup list; schema labels and snapshot positions are
authoritative. World is intentionally a truthful placeholder until public APIs
exist for world data, snapshots, backup, restore, or wipe.

## Design-only and future details

Server name, last refresh, start time, uptime, multiple completed activity rows,
durations, a View all link, and world-management data remain unavailable in
current public APIs. They are not first-screen data requirements.
