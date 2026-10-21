# Iteration 114 — PluginCatalog first-install Rollout

## Goal

Include PluginCatalog in the emergency **Rollout** without manual provider
setup or provider-specific code in Platform/Updater.

## Changes

- Platform registers `InstallHook=provisioning/provision.sh` for Catalog only.
- Generic Updater Prepare verifies and root-secures a versioned package.
- The generic root Executor runs the opt-in Catalog-owned `install` script
  before first activation; Catalog implements host → database → media.
- Subsequent Update/Rollback skip the hook. Retried installs reuse an existing
  immutable release only when its inventory and manifest exactly match the
  verified package. Existing databases, media and configs are preserved.
- Emergency Rollout includes Catalog after Snapshots, before Web.

## Rollout dependency

Publish **new** Development releases of Updater and PluginCatalog and apply
the updated Platform registry on the test VM. Catalog
`v0.1.0-dev.1` does not contain provisioning and cannot be used.

## Verification required

Run .NET Updater tests, Catalog provisioning contract smoke, Bootstrap tests,
and a real clean-VM first Install/Resume Rollout. No Production release or VM
installation is performed by the repository change itself.
