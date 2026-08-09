# HealthAutoArrange 1.1.3 review notes

This revision favors conservative UI behavior over reconstructing game medical state. The runtime source of truth remains the Moodle UI nodes actually created by the game. `AddMoodle` capture is metadata only.

## Why the new `#` pattern exists

CUCoreLib currently strips trailing digits from `Moodle.type` when recovering an icon key for animated moodles, which confirms that numeric suffixes are used in the current ecosystem. However, third-party icon IDs may themselves contain semantic digits. Therefore the GUI now generates `baseId#`, meaning “the exact base ID, or that base ID followed only by digits”. `*` remains intentionally broader and is not auto-generated.

## Why reminders are lifecycle-based

CUCoreLib documents custom Moodle queueing as something a mod should call during normal update logic while its condition remains active, with a default hold time of 0.75 seconds. Repeated AddMoodle/queue activity is therefore not a new medical event. Reminder episodes are based on observed presence/absence, not AddMoodle call count.

## Known non-ideal boundary

The game wiki states that side moodles may only appear in the health panel or while the Moodle bar is hovered. If the game destroys and recreates those nodes rather than merely hiding them, presence-based reminders can only see that UI lifecycle. A short absence grace reduces transient rebuild noise, but the mod deliberately does not read hidden Body variables to reveal information the vanilla UI is withholding.

## Build boundary

The execution container did not have dotnet/msbuild/csc/mono and its DNS could not resolve GitHub/NuGet/.NET download hosts. Source-level smoke checks were run; C# compilation and current-Steam-build tests were not fabricated.
