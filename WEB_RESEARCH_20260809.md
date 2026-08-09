# Web cross-validation — 2026-08-09

This review intentionally treats the game's current UI and mod API as moving targets rather than stable contracts.

## Findings that changed the implementation

### 1. The moodle bar is not one always-visible flat list

The community wiki describes lower/side moodles as appearing in the health panel or while the moodle bar is hovered. Unchipped mode changes which moodles are available, with some heart-rate moodles unique to that mode. Infection visibility is also conditional: the wiki states infection is revealed as a moodle/health-panel condition at 25% infection progress.

Implementation consequence: HealthAutoArrange sorts **only real Moodle UI nodes observed at runtime**, and keeps main/side rows separate. It does not create a supposedly complete medical-state list from wiki conditions.

Sources:
- https://scavprototype.wiki.gg/wiki/Moodles
- https://scavprototype.wiki.gg/wiki/Unchipped_mode
- https://scavprototype.wiki.gg/wiki/Infection

### 2. Third-party moodles can carry display semantics not inferable from their name

CUCoreLib's current `MoodleRegistry` exposes `intensity`, `critical`, `chippedOnly`, and `important`; its comments define `important` as main row versus side row. Queued custom moodles can also refresh/expire over time.

Implementation consequence: AddMoodle interception is metadata only. The state catalog is populated from actual Moodle components found under the live manager. Unknown states default to **Keep position**, not “send to end”.

Source:
- https://raw.githubusercontent.com/jimmyking9999999/CUCoreLib/main/Registries/MoodleRegistry.cs

### 3. Native settings integration is useful, but the current public registry is a poor fit for a dynamic reorder editor

CUCoreLib's current mod-options layer exposes conventional option definitions such as bool/int/float/dropdown/keybind. QoL: Unknown demonstrates that deep integration with the v7 settings menu is possible, and its changelog also contains fixes around setting registration, scrollbars, restoration, UI scale and resolution-dependent behavior.

Implementation consequence: v1.1.1 keeps the dynamic state catalog/order editor in its F8 window rather than duplicating the same settings in two authoritative stores. The F8 editor now uses progressive disclosure and hover `i` help. A future native settings bridge should be limited to simple master options unless a richer custom row/list API becomes stable.

Sources:
- https://raw.githubusercontent.com/jimmyking9999999/CUCoreLib/main/Registries/ModOptionsRegistry.cs
- https://raw.githubusercontent.com/jimmyking9999999/CUCoreLib/main/Data/ModOptionDefinition.cs
- https://www.nexusmods.com/scavprototype/mods/7

### 4. Version drift is a first-class risk

Current community mods still document remapping/checking identifiers against the current `Assembly-CSharp`. Public mod pages are not perfectly synchronized on exact game-version labels. That is evidence against treating a single demo build's field names/UI hierarchy as a permanent API.

Implementation consequence: the plugin logs game/Unity/Assembly-CSharp versions at startup, falls back between `UpdateMoodles` and `AddAllMoodles` refresh hooks, scopes captured metadata to the current manager, and uses conservative rendering fallbacks. A game update can still require source changes; no compatibility layer can guarantee otherwise.

Sources:
- https://www.nexusmods.com/scavprototype/mods/130
- https://www.nexusmods.com/scavprototype/mods/7

## Deliberate non-goals

- No hard-coded “medical truth” preset is enabled by default.
- No attempt is made to expose hidden game states before the game creates their moodles.
- No attempt is made to merge main and side rows.
- No promise is made that anchored-position reordering is valid for every future UI layout.
- No claim is made that the F8 IMGUI editor visually matches the native v7 settings menu.


## 1.1.3 follow-up — localization and runtime semantics

- CUCoreLib `MoodleRegistry.AddMoodle` currently documents `important` as main-row vs side-row selection, a stable queue key for a logical Moodle whose severity changes, and a default hold time of 0.75 seconds. It tells callers to queue while the condition remains active. Consequence: repeated queue/AddMoodle activity must not be treated as repeated medical events.
- CUCoreLib applies queued custom moodles by temporarily assigning `manager.sideMoodles = !important` and then calling vanilla `manager.AddMoodle(...)`.
- CUCoreLib's `Moodle.Start` animation patch strips trailing digits from `Moodle.type` to recover an icon key. This supports numeric severity suffixes in the current ecosystem, but does not prove that every third-party numeric suffix is severity. 1.1.3 therefore uses the stricter GUI-generated `#` severity-family matcher while keeping legacy `*`.
- The Scav Prototype Wiki says side moodles only appear in the health panel or while the Moodle bar is hovered. The mod therefore stays UI-observation based and keeps a short absence grace rather than reading hidden physiology to manufacture states.
- Current community precedent exists for bilingual standalone panels. Quick Medical Automation explicitly supports Chinese/English and has changelog fixes for mixed-language headers after switching; Casualty Vitals supports manual/Auto language selection.
