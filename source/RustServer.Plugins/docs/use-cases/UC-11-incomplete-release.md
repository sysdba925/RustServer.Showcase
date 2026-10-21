# UC-11 — Incomplete Release

## Goal
Make a partially created Release visible without allowing unsafe use.

## Example
```text
Plugins  ✓ release/3.0
Configs  ✕
```

## Flow
1. Operator runs Fetch.
2. System compares release branches from both repositories.
3. A version existing in only one repository is marked `Incomplete`.
4. UI may display it with a warning state.
5. Apply / Switch actions are disabled for that Release.
6. UI indicates which repository is missing the branch.

## Result
Operator understands why the Release is not yet usable.

## Fix
The Git state is corrected externally, then Fetch is run again.
