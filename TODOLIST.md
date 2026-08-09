# TODO / Release Gates

## P0 — must pass on the current Steam build

- [ ] Build against the user's current `Assembly-CSharp.dll`, Unity Managed DLLs and BepInEx installation.
- [ ] Confirm the selected refresh hook (`UpdateMoodles` or fallback `AddAllMoodles`) fires once at the expected update boundary.
- [ ] Confirm `manager.moodles`, `Moodle.type`, and `Moodle.isSide` still match the current runtime contract.
- [ ] Confirm Auto rendering actually changes the visual order without fighting animation/layout refreshes.
- [ ] Confirm main and side moodles remain in their native visibility/row behavior.
- [ ] Test normal/chipped and Unchipped modes.
- [ ] Test opening the health panel and hovering the moodle bar while side moodles appear/disappear.
- [ ] Test a state whose severity changes rapidly; verify no flicker/jitter and stable equal-priority ordering.
- [ ] Test an unknown third-party moodle; default Keep must not unexpectedly demote it.
- [ ] Test scene/menu/player rebuilds; stale capture metadata must not leak across managers.

## P1 — compatibility matrix

- [ ] QoL: Unknown installed and enabled.
- [ ] CUCoreLib installed with at least one custom main-row moodle and one side-row moodle.
- [ ] A mod with dynamic/expiring custom moodles.
- [ ] 16:9, ultrawide and at least one unusual resolution/UI-scale combination.
- [ ] Multiplayer client: confirm HealthAutoArrange remains presentation-only/local and does not alter synchronized health state.

## P1 — GUI polish after real screenshots

- [ ] Capture F8 window at 1080p and the user's actual UI scale.
- [ ] Verify the right-side `i` hover card never runs off-screen or blocks the row being configured.
- [ ] Check Chinese/English text truncation and button widths.
- [ ] Decide whether group rename belongs in Basic or stays Advanced based on actual use, not speculation.
- [ ] If users expect a native settings entry, add only a minimal master toggle/open-editor key there unless a stable dynamic-list API exists.

## P2 — optional enhancements

- [ ] Import/export a human-readable ordering preset.
- [ ] Add a read-only “currently visible” badge/count without polling every frame.
- [ ] Add an explicit compatibility diagnostic when the refresh hook or Moodle members are missing.
- [ ] Consider a user-authored medical-priority preset, clearly labeled as a preference preset rather than game truth.
