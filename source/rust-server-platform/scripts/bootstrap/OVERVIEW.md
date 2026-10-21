# Bootstrap overview

Run one command from the platform repository:

```bash
./scripts/bootstrap.sh
```

Execution chain:

```text
bootstrap.sh
  → 10 prerequisites
  → 20 development tools
  → 25 Node.js tools
  → 30 workspace/runtime directories
  → 35 local configuration, topology, and RCON secret
  → 40 RustServer.Rcon sync
  → 50 RustServer.Control sync
  → 60 RustServer.Versions sync
  → 63 RustServer.Plugins sync
  → 65 RustServer.Web sync
  → 70 RustServer.Rcon build/test/package
  → 75 RustServer.Rcon.Grpc Release build/verify
  → 80 RustServer.Control restore/build/test
  → 85 install/enable/start RustServer.Control systemd service
  → 86 install/enable/start RustServer.Rcon gRPC systemd service
  → 90 RustServer.Versions restore/build/test
  → 91 RustServer.Plugins restore/build/test/contract verify
  → 92 RustServer.Web npm ci/check
  → 93 configure Plugins credential; install/enable/start RustServer.Plugins service
       (Plugins itself creates/clones `oxide` and `oxide/plugins`)
  → 94 install/enable/start RustServer.Web systemd service
  → 95 install/enable/start RustServer.Versions systemd service
  → 99 final host/tool validation
```

Canonical workspace layout:

```text
<workspace>/
├── rust-server-platform/
├── oxide/                         # Plugins-owned ConfigFiles; created on first Plugins start
│   └── plugins/                   # Plugins-owned PluginFiles; created on first Plugins start
├── repos/
│   ├── RustServer.Rcon/
│   ├── RustServer.Control/
│   ├── RustServer.Versions/
│   ├── RustServer.Plugins/
│   └── RustServer.Web/
├── state/
│   ├── RustServer.Plugins/         # PluginManagement persistent state
│   └── RustServer.Snapshots/
├── packages/
│   ├── nuget/
│   └── global-packages/
└── NuGet.config
```

The Rust runtime remains at `/srv/rust/server`.

## Runtime configuration status

This overview describes the current development Bootstrap. Stage 1 implements
the approved Rust Dedicated Server settings path from the flat layout in
[`docs/architecture/runtime-configuration-layout.md`](../../docs/architecture/runtime-configuration-layout.md):
step 35 safely creates or migrates `/etc/rustserver/rust-server.env` and
preserves a legacy checkout `.env` only as a recoverable backup. The RCON
secret and other runtime-configuration migrations remain separate work; do
not move them manually.

Rules:

- Any non-zero step stops bootstrap immediately.
- Each numbered step owns its paths, inputs, actions, and validations; service-specific checks do not belong in step 99.
- A new operation normally means a new numbered script.
- `bootstrap.sh` is a dumb ordered runner and should remain unchanged.
