```
rust-server-platform/
├── docs/
│   ├── architecture/
│   ├── decisions/
│   └── plans/
├── services/
├── scripts/
├── docker/
├── terraform/
└── README.md
```

## Bootstrap

### Runtime configuration status

The approved Development and Production target is documented in
[Runtime Configuration Layout](docs/architecture/runtime-configuration-layout.md).
Stages 1, 2, 6, and 7 are implemented for Rust Dedicated Server settings, the
RCON secret, Control's desired/effective state, and installed Compose runtime
assets. The canonical persistent files are
`/etc/rustserver/rust-server.env`, `/etc/rustserver/rcon-password`, and
`/var/lib/rustserver/control/server-configuration.json`. Bootstrap safely
preserves or migrates their legacy values; Step 82
installs the Rust, SteamCMD, and Oxide Compose assets at
`/opt/rustserver/runtime/`; do not move them manually.

On a manually prepared Ubuntu 24.04 host with Git, OpenSSH/GitHub access, and `sudo` already available, run the complete development/server bootstrap from this repository root:

```bash
./scripts/bootstrap.sh
```

It installs the required development tools, prepares the canonical shared workspace, safely creates or migrates Rust settings at `/etc/rustserver/rust-server.env`, preserves the current RCON secret, migrates Control state to `/var/lib/rustserver/control/server-configuration.json`, installs the versioned Compose definitions and scripts at `/opt/rustserver/runtime/`, and creates or preserves `/etc/rustserver/topology.env`, safely synchronizes the service repositories and the Plugins Development workspaces, builds the internal NuGet package and RCON gRPC host, validates the Plugins API contract, installs the RCON gRPC, Control, Versions, Plugins, Snapshots, and Web services, prepares and validates the frontend, then performs final read-only validation. For prerequisites, exact step behavior, rerunning safely, configuration and secret policy, and NuGet details, see the [bootstrap guide](scripts/bootstrap/README.md).

## UI design

Product/UI contracts and the selected Prototype #1 are documented under
[`docs/ui/`](docs/ui/).

`RustServer.Web` is a separate frontend repository. Development Bootstrap
continues to prepare its Vite workflow; production Bootstrap serves verified
StaticWeb releases through Nginx and does not require Node.js or Vite.
