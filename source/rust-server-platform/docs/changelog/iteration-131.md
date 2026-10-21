# Iteration 131 — Production loopback listeners

- Default the Production Updater and all generated topology bind hosts to loopback; reject non-loopback overrides when creating a new Updater environment.
- Specify the Auth loopback URL in newly provisioned units; preserve existing configurations and units for explicit operator migration. Document listener verification and retain game and SSH access.
