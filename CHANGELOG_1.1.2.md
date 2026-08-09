# HealthAutoArrange 1.1.2

## Fixed

- Persistent-state reminder spam: legacy/default zero cooldown no longer means “send on every refresh”.
- F8 reminder editor now actually exposes frequency controls; the old cooldown drawing method existed but was not called.
- Added **Once per appearance** and **While present** reminder lifecycles.
- While-present reminders use `PeriodSeconds / SendsPerPeriod`, fire immediately on appearance, and never burst-catch-up missed slots.
- While-present frequency is capped to a one-second minimum effective interval; extreme period/count values are normalized instead of promising an unreachable or spammy rate.
- Repeat periods are capped at seven days to avoid `DateTimeOffset.AddSeconds` overflow from extreme hand-edited values.
- State disappearance resets the episode after a 1-second confirmation grace; sub-second UI rebuild/side-row flicker does not create a new episode. Later genuine reappearance can alert immediately.
- Saving/reloading configuration clears stale presence snapshots before a new reminder engine starts.
- Reminder cadence no longer depends solely on game Moodle rebuild frequency; a 4 Hz timer advances the state machine from the last UI-confirmed presence snapshot while gameplay is unpaused.
- Log mode is now log-only. BottomAlert is overlay-only; duplicate native `PlayerCamera.DoAlert` delivery was removed.
- Presentation-layer long cooldown was removed from formal reminders; ReminderEngine is the single cadence authority.
- Repeated visual alerts for the same state replace their own active overlay item instead of stacking duplicate copies.
- Numeric reminder parsing is invariant-culture safe.
- Invalid `PlacementPreset` no longer accidentally resets to enum default; visual preset parsing applies the preset before explicit overrides.
- Reminder wildcard removal/dedup uses semantic `PatternBaseId`, preserving third-party IDs with meaningful trailing digits.
- Wall-clock rollback rebases the next repeat due time without an extra full-interval delay.
- Reminder runtime state is isolated per rule instance; accidental duplicate rule names no longer share one episode/cadence state.

## Compatibility / migration

- Old positive `CooldownSeconds=N` -> `RepeatMode=WhilePresent`, `PeriodSeconds=N`, `SendsPerPeriod=1`.
- Old `CooldownSeconds=0` -> `RepeatMode=Once` (intentional safety change).
- New saves write `RepeatMode`, `PeriodSeconds`, and `SendsPerPeriod`; legacy cooldown remains readable.
- Partial 1.1.2 frequency fields migrate field-by-field, so adding only `PeriodSeconds` does not accidentally discard a positive legacy repeat mode.

## Verification in this environment

- `git diff --check`: passed.
- Static/source contract smoke: 42/42 passed.
- C# compile/unit execution: not available here because the container lacks .NET/Mono tooling and package/network access failed.
- In-game verification: still required on the current Steam build.
