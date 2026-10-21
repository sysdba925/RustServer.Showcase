# rust-server-platform — правила для агентов

Перед изменением Bootstrap, Updater integration, systemd или Nginx прочитайте
`docs/architecture/` по затронутой области, `deploy/bootstrap/README.md` и
текущую запись из `docs/changelog/`.

## Границы Platform

- Platform владеет общим host bootstrap, корнем `/opt/rustserver`, Updater
  registry и generic Nginx infrastructure. Updater устанавливает только
  проверенные release artifacts через `Prepare`, `Install`, `Update` и
  `Rollback`.
- Каждое приложение владеет своими provider-specific dependencies и explicit
  provisioning. В частности, Platform и Updater не знают Catalog database
  provider, connection string, role, schema, media provider или media root и
  никогда не создают их как побочный эффект Bootstrap, Update,
  Rollback, systemd restart либо Nginx reload. Только явная первая установка
  через зарегистрированный проверенный application-owned InstallHook может
  создать необходимые зависимости.
- Platform может зарегистрировать `RustServer.PluginCatalog` в root-owned
  Updater registry с явным `InstallHook=provisioning/provision.sh`. Этот opt-in
  разрешает только общему privileged Updater Executor выполнить проверенный
  Catalog-owned install hook на первой установке. Platform не содержит
  provider-specific команд и не запускает provisioning при Bootstrap.
- Platform exposes one root-owned, non-writable-by-services Nginx application
  include directory. Application-owned host provisioning may place a reviewed
  root-owned snippet there; every public application route must keep its own
  explicit authorization boundary and upstreams must remain non-public.

## Безопасность и повторный запуск

- Existing configuration, releases, databases, media and systemd units are
  state: preserve them. A conflicting file must stop with an actionable error
  or take an explicit backup; never silently overwrite, reset, drop or clean
  it.
- Keep Bootstrap idempotent and conservative. Do not turn validation or
  readiness into provisioning, and do not run real installations, cleanup or
  release publication while validating a change.
- Keep `applications.ini` root-owned and read-only to Updater. Do not add an
  API path for changing registrations or passing privileged commands.

## Проверка и документация

- Add a small representative regression/smoke test for changed behavior.
- Run shell syntax checks, the focused Bootstrap/Updater tests, and
  `git diff --check` where applicable. State checks not run.
- Update the applicable architecture/operator documentation and add a concise
  central changelog entry for material boundary changes.
