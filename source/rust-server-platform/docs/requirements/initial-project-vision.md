# Initial Project Requirements and Vision

## Purpose

`rust-server-platform` is intended to become a management, deployment, and infrastructure platform for a self-hosted Rust game server on a VPS.

The initial objective is convenient, manually initiated server management through a visual management interface (potentially a web UI), so routine administration does not require SSH sessions and shell aliases. The platform must remain extensible for later deployment, monitoring, automation, and infrastructure capabilities.

This document records requirements and vision. It is not an architecture specification and does not select implementation technologies.

## Current Repository Landscape

The Rust server is currently represented by four Git repositories/layers:

1. **Rust server repository**: located around the installed server; tracks selected configuration and text files only. Runtime directories, binaries, generated files, Steam files, and other unrelated server content are ignored. It is not a complete copy of the server installation.
2. **Oxide plugins repository**: covers the Oxide/uMod plugins directory and tracks C# plugin source files (`*.cs`) only.
3. **Oxide configuration/data repository**: operates over the general Oxide directory structure, tracking configuration and data rather than C# plugin sources. Unrelated runtime files are ignored. This layer is mutable and requires careful review.
4. **Special scripts repository**: contains operational server-maintenance and management scripts, including start, stop, update, and potentially deployment operations. Its precise responsibility may evolve.

## Desired Management Capabilities

The future management layer should make normal remote VPS operations available without SSH, including:

- starting and stopping the Rust server;
- updating Rust through SteamCMD;
- updating server-related files and Oxide/uMod;
- updating C# plugins and plugin configuration/data;
- executing multi-stage deployment or update flows.

Operations may be executable independently where appropriate. The UI and implementation approach are intentionally undecided.

## Operation Execution and Visibility

Long-running operations must not be treated as fire-and-forget commands. The management system should expose at least these states: pending, running, succeeded, and failed. Operations should expose progress or individual stages where useful.

Errors must be visible in the management interface as well as normal log files. A failed required stage must prevent dependent stages from continuing as if it had succeeded.

An illustrative, non-finalized flow is:

```text
Stop server
    ↓
Update Rust / SteamCMD
    ↓
Update server files
    ↓
Update Oxide
    ↓
Update plugins
    ↓
Update configuration
    ↓
Start server
```

## Configuration and Release Principles

Plugin configuration is critical state, not disposable deployment output. Plugin upgrades can introduce or change configuration fields, generate values, or modify configuration after startup. Production configuration must therefore not be destroyed by unconditional operations such as `git reset --hard`.

The intended release path is approximately:

```text
Obtain/update plugin → commit plugin source → deploy to test/staging → validate
→ review generated or changed configuration → commit validated configuration
→ create releases → explicitly promote validated releases to production
```

A normal Git push must not alter production. Plugin updates may initially be downloaded and added manually. Plugin code and configuration may be versioned or released independently, while their compatibility must eventually be manageable.

The Git workflow, configuration reconciliation, release coordination, and source-of-truth model remain undecided.

## Planned Version and State Information

A small future service may collect and expose installed and available component versions/state, for example Rust server and Oxide versions, and deployed versus available plugin and configuration releases. Its API, ownership, and implementation are not yet defined.

## Future Scope Boundaries

- Supporting management/infrastructure components are expected to use Docker containers, but service boundaries are undecided. Do not assume one container per operation or that the Rust game server runs in Docker.
- Terraform is planned for VPS provisioning, base setup, bootstrap, and deployment preparation. It is not yet intended to manage routine plugin deployments.
- Monitoring may later cover host, Rust server, management service health, operation status, logs, and game/server metrics. No monitoring stack has been chosen.
- Third-party purchased plugin updates may initially be manual. Marketplace integrations and automatic updaters are future capabilities.

## Initial Practical Target

The first useful version should eventually make it practical to:

1. Prepare a clean VPS/server.
2. Install or update Rust and Oxide/uMod.
3. Deploy approximately 50 purchased plugins and validated plugin configurations.
4. Start and stop the server.
5. Perform controlled updates.
6. See whether operations succeeded or failed and inspect relevant errors without manually operating everything through SSH.

## Established Principles

- Keep Git-controlled desired state separate from generated and runtime state.
- Do not treat the complete Rust installation as a Git repository.
- Promote tested releases to production explicitly.
- Make failures visible and stop dependent stages after a required failure.
- Remain extensible for monitoring, automation, Terraform provisioning, plugin marketplace integrations, and additional services.
- Avoid premature architecture decisions.
- Start simply enough to operate one Rust server through a management layer.

## Unresolved Architecture Topics

The following are intentionally unresolved and require later design:

- main control plane and choice of web UI, existing management software, or custom application;
- location of orchestration logic and host command execution;
- Docker versus direct VPS responsibilities;
- long-running job, progress, log-streaming, and error-exposure model;
- source of truth and reconciliation for mutable Oxide configuration;
- association of plugin and configuration releases;
- Git branching, tagging, release, staging/production, and rollback strategy;
- ownership of the version/state service;
- Terraform responsibilities versus those of the deployment/control layer;
- secrets management, monitoring stack, and future third-party plugin update automation.
