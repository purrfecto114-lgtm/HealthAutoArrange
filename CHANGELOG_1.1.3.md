# HealthAutoArrange 1.1.3

## GUI / localization

- Added an in-window `EN / 中文` button in the upper-right corner.
- Language switches immediately without reopening the window and persists to `[UI] Language`.
- Centralized Mod-owned GUI text, hover help, reminder labels, preview fallback and diagnostics feedback in `UiTextCatalog`.
- Kept game/third-party Moodle display names untouched instead of treating translated display text as a stable state identifier.
- Reflowed reminder numeric/custom-position controls for narrow windows so longer English labels are less likely to overlap.
- Fixed custom-placement preset fields appearing one frame late after switching to Custom.
- Localized technical tooltip booleans (`是/否`, `yes/no`).

## Reminder correctness

- Preserved 1.1.2 `Once` / `WhilePresent` lifecycle semantics and legacy cooldown migration.
- Reconfiguration now preserves continuous episode state for cadence-equivalent rules; saving unrelated settings does not retrigger `Once`.
- Sorting master toggle now controls sorting only. Moodle observation and individually enabled reminders keep running when sorting is disabled.
- Kept no-catch-up repeat scheduling and one-second minimum effective interval.
- Same-state on-screen repeat alerts refresh one visual slot instead of stacking duplicates.
- Log-only mode remains non-visual; transparent alerts no longer double-route through native `DoAlert`.

## Runtime matching / compatibility

- GUI-generated rules now use `baseId#`: exact base or base followed only by numeric severity digits.
- Legacy/manual `baseId*` remains a broad prefix wildcard for backward compatibility.
- This prevents generated rules such as `pain*` from accidentally capturing unrelated shared-prefix states such as `painshock`.
- AddMoodle metadata fallback now uses the same conservative severity-family logic and preserves semantic numeric icon IDs (`drug2`, `drug3`, etc.).
- When capture metadata is missing, the observed catalog keeps the exact runtime ID instead of guessing that trailing digits are severity.
- If reliable capture metadata later returns, the provisional exact-runtime catalog row is merged into the reliable base-ID row.
- Capture resolution remains manager-scoped and refresh-window-scoped to reduce stale metadata after scene/UI rebuilds.
- Diagnostics no longer invent a stripped base ID when capture metadata is missing.

## Numeric robustness

- Rejects/normalizes NaN and Infinity in reminder period, opacity, duration and placement values.
- Keeps invariant-culture persistence/parsing for numeric config fields.

## Validation

- `tools/static_smoke.py`: 56/56 source-contract checks pass in the provided execution environment.
- Real `dotnet test`, plugin compilation and in-game smoke tests are still required on a machine with the current game Managed assemblies.
