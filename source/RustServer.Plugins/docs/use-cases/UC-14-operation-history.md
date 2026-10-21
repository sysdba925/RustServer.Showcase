# UC-14 — Review Persistent Operation History

## Goal

Find and inspect past Plugins mutation results, including operations that
finished before the current service process started.

## Flow

1. Operator opens the operation history.
2. System shows compact summaries from newest to oldest.
3. Operator loads older summaries with the provided cursor when needed.
4. Operator selects an entry.
5. System shows the complete detailed report by operation ID.

## Restart recovery

If the service restarted while a report was `inProgress`, the next startup
marks it `interrupted` with `OperationInterrupted`. This is a diagnostic
outcome only: the service does not assume Git, Runtime, or Baseline recovery.
The operator reviews the report and observable current state before deciding
what to do next.

## Result

Operation history remains available across restart. Newer operations added
while older pages are viewed do not duplicate entries on a cursor continuation.
