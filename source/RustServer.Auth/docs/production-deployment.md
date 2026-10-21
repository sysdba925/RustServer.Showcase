# Production deployment

`RustServer.Auth` is a framework-dependent .NET application. Its production
configuration is intentionally separate from both the release directory and
`launchSettings.json`.

On a Production Bootstrap host, `/etc/rustserver/auth.env` is the only
persistent Auth environment file. Bootstrap creates the service identity,
state directory, release boundary, and systemd unit, but it never creates or
modifies this file.

## Deployment contract

The expected host layout is:

```text
/opt/rustserver/auth/releases/<version>/  immutable extracted release
/opt/rustserver/auth/current              symlink to the active release
/var/lib/rustserver/auth/auth.db           persistent SQLite database
/etc/rustserver/auth.env                   root-owned environment configuration
```

The production configuration bundled with the application listens only on
`http://127.0.0.1:5286` using HTTP/1.1. It is not a public listener. A local
reverse proxy is responsible for TLS termination and any public exposure.

`appsettings.Production.json` provides the default database location and
listener. It also explicitly trusts only `127.0.0.1` and `::1` as reverse
proxies for the standard `X-Forwarded-For`, `X-Forwarded-Proto`, and
`X-Forwarded-Host` header set. This lets Auth generate public HTTPS OAuth
callback URLs after a local Nginx instance terminates TLS;
headers from any other client are ignored. If Nginx does not connect from one
of those addresses, set `ReverseProxy__TrustedProxies__0` (and additional
numbered values as needed) to its exact source IP address. Operators can
override the database or listener using normal ASP.NET Core environment
configuration, for example `ConnectionStrings__AuthDatabase` and
`Kestrel__Endpoints__Http__Url`. Do not put a database inside a release
directory: Updater may replace that directory atomically.

## Host preparation

Production Bootstrap performs this host preparation create-only. The commands
below are for a host managed manually instead; do not rerun them to replace an
existing service unit, state directory, release, or configuration file.

Create a dedicated, non-login service account and the persistent data directory:

```sh
sudo useradd --system --home-dir /var/lib/rustserver/auth --shell /usr/sbin/nologin rustserver-auth
sudo install -d -m 0750 -o rustserver-auth -g rustserver-auth /var/lib/rustserver/auth
sudo install -d -m 0755 -o root -g root /opt/rustserver/auth/releases
```

The included unit also declares `StateDirectory=rustserver/auth`, so systemd
creates `/var/lib/rustserver/auth` with the service account as owner on hosts
that support `StateDirectory`. Releases under `/opt/rustserver/auth/releases`
must be readable by `rustserver-auth`; keep them owned by a deployment account
or `root`, never by the service account. The `current` symlink may be replaced
atomically while the service is stopped or restarted.

The service runs with `/opt/rustserver/auth/current` as its working directory,
so ASP.NET Core loads `appsettings.json` and `appsettings.Production.json`
from the active release. The SQLite database remains at the absolute Production
path `/var/lib/rustserver/auth/auth.db`.

Install the unit without editing secrets into it:

```sh
sudo install -m 0644 deploy/rustserver-auth.service /etc/systemd/system/rustserver-auth.service
sudo systemctl daemon-reload
```

Provision the populated `/etc/rustserver/auth.env` through the host
secret-management process. If it already exists, it is authoritative: do not
copy `deploy/auth.env.example` over it, regenerate values, or move them into a
second Auth configuration location. The file must be a regular file owned by
`root:rustserver-auth`, mode `0640` (or stricter while preserving the required
reader). Do not change the shared `/etc/rustserver/` directory ownership or
mode. systemd reads `EnvironmentFile=/etc/rustserver/auth.env` at launch and
passes the values to the `rustserver-auth` process.

## Configuration and first initialization

The service always requires these environment variables because Google
authentication is registered at startup:

```dotenv
GOOGLE_CLIENT_ID=...
GOOGLE_CLIENT_SECRET=...
```

`Bootstrap__AdminUserName` is a non-secret identifier for the bootstrap
administrator. On the first start of an empty database, also provide:

```dotenv
ADMIN_USERNAME=...
ADMIN_PASSWORD=...
```

`ADMIN_USERNAME` must match `Bootstrap__AdminUserName` when the latter is set.
The password must comply with the configured ASP.NET Core Identity policy
(currently a minimum of 12 characters). After a successful first start, remove
`ADMIN_USERNAME` and `ADMIN_PASSWORD` from `/etc/rustserver/auth.env`; later
starts never require or read them. When a bootstrap name is configured, the
service checks that this existing user remains present and restores its Admin
and Player roles if necessary. It never creates a different administrator when
users already exist.

The application only loads a developer `.env` file in the Development
environment. Production secrets must come from the systemd `EnvironmentFile`
or another external environment provider, not a release file. The platform
deployment contract leaves optional connection-string and listener overrides
unset, so the persistent SQLite database remains at the default path below.

On a Production Bootstrap host, do not enable or start Auth merely because the
unit exists. First provision the environment, then use the explicit Updater
`Prepare RustServer.Auth <version>` and `Install RustServer.Auth <version>`
operations. The Deployment Executor validates the required configuration
before it switches the release or invokes systemd; a successful, ready first
installation enables the unit for later boots.

## Database and migrations

SQLite is configured as `Data Source=/var/lib/rustserver/auth/auth.db` in
Production. The directory must exist and be writable by `rustserver-auth`.
Before the service accepts requests, startup applies EF Core migrations, then
ensures the required roles and performs the first-admin workflow when needed.

Migration errors are logged to the service journal and abort startup. The
application does not call `EnsureCreated`, delete, reset, or replace an
existing database. Consequently, a failed migration requires operator action;
do not delete `auth.db` to make the service start. Back up the persistent data
directory before deployment and test migrations against a copy where practical.

## Readiness and lifecycle

Bootstrap and Updater should use this probe after starting or restarting the
service:

```text
GET http://127.0.0.1:5286/health/ready
```

The endpoint is deliberately unauthenticated and returns no sensitive
diagnostic information:

| Result | Meaning |
| --- | --- |
| `200 OK` | SQLite is reachable, all required Identity tables are present, and no EF migrations are pending. |
| `503 Service Unavailable` | SQLite cannot be used, the required schema is unavailable, or migrations remain pending. |

If startup migration or configuration validation fails, the process exits
before readiness can return `200`. systemd restarts it according to the unit's
`Restart=on-failure` policy. On `SIGTERM`, ASP.NET Core stops accepting work
and performs its normal graceful shutdown; systemd allows 30 seconds before
forcing termination.

## Release switching

Bootstrap or Updater downloads and verifies the existing
`RustServer.Auth-v<version>-linux-x64.tar.gz` artifact and its SHA256 sidecar,
extracts it into a new immutable `/opt/rustserver/auth/releases/<version>/`
directory, then atomically repoints `current`. This repository does not add a
separate deployment mechanism or modify those components.

After the switch, restart `rustserver-auth.service` and poll the loopback
readiness URL until it returns `200`. The data directory and environment file
remain untouched by release replacement, so Google credentials and the
existing SQLite database survive application upgrades. The published archive
still has `RustServer.Auth.dll` at its root, so the systemd `ExecStart` path
remains stable across versions.
