# Iteration 012 — UI control-plane prototypes

## Goal

Record the first three UX prototypes for the Rust server management control
plane, so their layouts, state transitions, and interaction models are
available for future product and implementation decisions.

## Added documentation

- `docs/ui/prototypes/01-overview.md` — product-oriented Overview dashboard:
  Server is the primary runtime card, followed by Rust, Oxide, and Activity.
- `docs/ui/prototype-002-control-room.md` — operational control room:
  central read-model, a persistent command panel, dense resource inspection,
  and an operation-centered active state.
- `docs/ui/prototype-003-workflow-first-control-plane.md` — workflow-first
  control plane: user intent is translated into an inspectable plan, execution,
  verification, and an explicit result.

## Notes

- The prototypes preserve concrete ASCII layouts for normal, stopped, starting,
  maintenance, successful, and failed operation states where applicable.
- They are UX concepts, not implementation commitments or confirmation that
  every depicted orchestration workflow is already provided by the backend.
- Together they provide three distinct directions for evaluating the future UI:
  product dashboard, operator control room, and workflow-first control plane.
