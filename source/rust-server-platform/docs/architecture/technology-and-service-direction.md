# Technology and Service Direction

## Status and Scope

This document records the accepted direction for the platform. It guides incremental implementation but is not a complete architecture or delivery plan. Decisions explicitly marked unresolved are intentionally not made here.

## Technology Direction

### Management UI

The management UI will be a separate web application built with React and TypeScript.

The frontend architecture should remain simple and approachable: React is both a project technology and a practical learning goal for the project owner. The UI must not contain business logic or server-management logic that belongs to backend services.

### Backend

C# and ASP.NET Core are the primary backend technologies. ASP.NET Core Web API should be used where appropriate. Entity Framework and PostgreSQL may be introduced later if persistence becomes necessary.

This direction aligns with the project owner's experience as a Unity/C# developer using ASP.NET Core, Entity Framework, and PostgreSQL.

## General Architecture Direction

The platform will evolve toward a microservice architecture as a practical learning project as well as a product direction.

Services must have concrete responsibilities. The platform should neither become a monolith solely for convenience nor be split into artificial microservices without a meaningful boundary.

The intended high-level direction is:

```text
Admin UI
    ↓
independent ASP.NET Core services
    ↓
server / deployment / version / other responsibilities
```

The architecture must be refined from real services as they are introduced rather than designed completely in advance.

## Runtime configuration

The approved runtime configuration contract for Development and Production is
[`runtime-configuration-layout.md`](runtime-configuration-layout.md). It owns
the persistent configuration boundary, installed-runtime asset location, and
Rust Dedicated data location. Its migration is pending; this direction document
does not claim that current Bootstrap, systemd, Docker Compose, or services
already use that layout.

## Initially Identified Responsibilities

### Server Control

A backend component is needed to manage the Rust server. It should eventually provide the Admin UI with operations such as start, stop, restart, other server-management operations, and the state of an executing operation.

Its exact service boundary is not yet approved.

### Deployment

A separate deployment capability is needed. It should eventually cover initial deployment, deployment of Rust server-related components, plugins, plugin configuration/data, and transitions between validated releases.

It remains undecided whether this will be one deployment service or multiple services.

### Version Service

A small, independent version service is planned. Its responsibility is to query different sources and normalize version information. Possible sources include local files, installed software, HTTP endpoints, external web resources or APIs, and other version sources.

It must remain focused on version/state information and must not take on deployment or server-control responsibilities.

## Admin UI and Long-Running Operations

One Admin UI will be the user's interaction point with backend services. Its pages, tabs, and sections must emerge from actually implemented capabilities; they are not defined in advance.

Long-running update and deployment operations require the UI to receive state changes without a manual page refresh. A realtime or push approach may be considered, but no mechanism is selected.

The UI may present a bounded live output or console for the active operation, such as a buffer containing the latest several thousand characters. This should help users see current progress, identify a stalled operation, and understand failures without overwhelming or freezing the UI. Full logs may be stored separately. Streaming, buffering, and log storage mechanisms are not decided.

## Future Services

The direction must allow later independent backend services for capabilities such as users, VIP/status systems, purchases/payments, and other game/server-related features. These services are not designed or created at this stage.

## Repository, Build, and Container Direction

`rust-server-platform` is not required to contain source code for every application or service. The preferred direction is for applications and services to have their own repositories, while this repository focuses on infrastructure, deployment, and integration concerns.

Built application artifacts may be placed in an appropriate platform-repository or environment directory for deployment or Docker image creation. The artifact format, registry, CI/CD process, build location, image creation responsibility, deployment mechanism, and participating repositories are not yet defined.

Built applications may later be packaged in Docker containers. This does not establish a concrete containerization or deployment topology.

## Explicitly Unresolved

- Exact boundaries of the server-control service.
- Exact boundaries and number of deployment services.
- Whether a separate orchestration service is required.
- How the Admin UI receives live operation progress.
- How bounded live log/output is presented.
- Where full operation logs are stored.
- Whether a message broker is required between services.
- Whether a shared database is needed and which services require persistence.
- How services discover each other.
- The exact Admin UI structure.
- Repository structure.
- Build and release pipeline.
- Docker packaging.
- Artifact storage or registry.
- Deployment topology.
