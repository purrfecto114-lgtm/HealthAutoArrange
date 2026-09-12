// Faithful re-implementation of the decompiled Casualties: Unknown Demo 7.0.1
// MoodleManager rebuild cycle.
//
// GROUND TRUTH PROVENANCE (v1.2.3 sim upgrade): the user provided the real game
// runtime Managed.zip (Assembly-CSharp.dll, 879,104 bytes, real method bodies -
// not the publicised NuGet reference assembly whose bodies are all `throw null`).
// It was decompiled with ilspycmd 9.1 and BYTE-VERIFIED against the publicised
// 7.0.1 ref (identical type/member sets; only visibility differs). This fake now
// mirrors the REAL 7.0.1 bodies line-for-line where position is concerned:
//   Update()            : updateTime -= unscaledDeltaTime; when <= 0 -> UpdateMoodles()
//   UpdateMoodles()     : sideMoodles=false; updateTime=0.5; UpdatePrevMoodles();
//                         ClearMoodles(); AddAllMoodles();
//   ClearMoodles()      : Object.Destroy(each child) [DEFERRED to end of frame];
//                         moodleCount=0; mainCount=0
//   AddAllMoodles()     : if (!body.alive) return;  <- REAL 7.0.1 death early-return
//                         if/else chain -> AddMoodle(...) per body state;
//                         sideMoodles=true; side AddMoodle(...)s;
//                         bonus "+N" Instantiate (no Moodle component) at
//                         (mainCount * 70 - 10, 0) when moodleCount - mainCount > 0
//   AddMoodle(...)      : if (chippedOnly && WorldGeneration.unchipped) return (no-op!);
//                         else new GameObject, SetParent(moodles) [appends LAST sibling],
//                         anchoredPosition = (moodleCount * 70,
//                                 critical ? Sin(unscaledTime * 6) * 4 : 0),  <- REAL wobble base
//                         type = icon + intensity, isSide = sideMoodles,
//                         doWarningFlash = critical && showSideMoodles,         <- REAL flash gate
//                         if !prevMoodles.Contains(type): y += 75, scale = 2.5 (pop-in),
//                         mainCount/moodleCount++
//   Moodle.Update()     : anchoredPosition = (x PRESERVED,
//                                 Lerp(y, doWarningFlash ? Sin(t*6)*4 : 0, dt*12));  <- REAL wobble chase
//                         localScale = Lerp(scale, one, dt*12)
//
// showSideMoodles semantics (real property): true unless (manager.sideMoodles &&
// !alwaysShowHidden && mouseLow) - in which case woundView.activeSelf. During MAIN
// moodle creation the manager's sideMoodles field is always false (UpdateMoodles
// resets it before AddAllMoodles), so MAIN criticals ALWAYS get doWarningFlash
// (they wobble forever). Side criticals flash only while visible. The fake models
// the mouse/wound component with a single static toggle.
//
// Harmony simulation: the fake game calls the exact adapter callbacks that the real
// GamePatches routes, in the same order:
//   per AddMoodle: OnMoodleAdded (prefix) -> [game body] -> OnMoodleCreated (postfix)
//   ClearMoodles : OnMoodlesCleared (postfix)
//   AddAllMoodles: OnMoodlesUpdated (postfix)   [v1.2.2 lowest boundary; fires after
//                  the death early-return too, exactly like a real postfix]
//   UpdateMoodles: OnMoodlesUpdated (postfix)   [same frame; adapter dedupes]
using System;
using System.Collections.Generic;
using HealthAutoArrange.Core;
using HealthAutoArrange.Plugin;
using UnityEngine;

    /// <summary>The game's Moodle component (decompiled fields actually used by the mod + the
    /// wobble flag that drives Moodle.Update's y target).</summary>
    public sealed class Moodle : MonoBehaviour
    {
        public string type;
        public bool isSide;
        public bool doWarningFlash;

        /// <summary>v1.2.3: REAL field - Moodle.Start()/Update() fade new states in by
        /// alpha = 1 - unTransparentTime*2 (AddMoodle sets 0.5 for newly-appearing types).
        /// The mod's pre-fade fix reads it in the creation frame.</summary>
        public float unTransparentTime;
    }

    /// <summary>Fake PlayerCamera: only the public members the mod's pre-fade reads
    /// (blackAmount field + static GetUnconsciousBlack()), per the real 7.0.1 decompile.</summary>
    public sealed class PlayerCamera : MonoBehaviour
    {
        public static PlayerCamera main;
        public float blackAmount;
        public static float GetUnconsciousBlack() => 0.5f;
    }

    /// <summary>Stands in for the game's WorldGeneration.unchipped static.</summary>
    public static class WorldGeneration
    {
        public static bool unchipped;
    }

    /// <summary>
    /// Faithful fake MoodleManager. Constructor takes the adapter so the simulated Harmony
    /// patch boundaries can route into it exactly like GamePatches does at runtime.
    /// </summary>
    public sealed class MoodleManager : MonoBehaviour
    {
        public Transform moodles;
        public bool sideMoodles;
        /// <summary>Models body.alive: real AddAllMoodles starts with
        /// `if (!body.alive) return;` (moodles vanish on death; postfix still fires).</summary>
        public bool alive = true;
        /// <summary>Models the mouse/wound half of the real showSideMoodles property.
        /// Side criticals flash (and wobble) only while this is true; main criticals
        /// always flash because the manager's sideMoodles field is false during the
        /// main-row creation block.</summary>
        public static bool SideMoodlesVisible = true;

        internal float updateTime = 0.5f;
        internal int moodleCount;
        internal int mainCount;
        private readonly List<string> prevMoodles = new List<string>();

        private readonly UnityUiAdapter _adapter;
        // The game's body state drives AddAllMoodles' hardcoded if/else chain. The test
        // scenarios poke this list directly to simulate player state changes.
        internal readonly List<(string icon, int intensity, bool critical, bool chippedOnly)> CurrentStates
            = new List<(string, int, bool, bool)>();

        public MoodleManager(UnityUiAdapter adapter)
        {
            _adapter = adapter;
            // Attach to a host GameObject like Unity does for every MonoBehaviour, so
            // Object.Destroy(manager.gameObject) in the manager-replacement scenario
            // works and gameObject/name behave like the real engine.
            var host = new GameObject("MoodleManagerHost");
            host.Components.Add(this);
            Owner = host;
            var container = new GameObject("MoodlesContainer");
            moodles = container.transform;
        }

        public void Update()
        {
            updateTime -= Time.unscaledDeltaTime;
            if (updateTime <= 0f)
            {
                UpdateMoodles();
            }
        }

        public void UpdateMoodles()
        {
            sideMoodles = false;
            updateTime = 0.5f;
            UpdatePrevMoodles();
            ClearMoodles();
            AddAllMoodles();
        }

        public void ClearMoodles()
        {
            foreach (Transform moodle in moodles)
            {
                UnityEngine.Object.Destroy(moodle.gameObject);
            }
            moodleCount = 0;
            mainCount = 0;

            // Simulated ClearMoodlesPostfix.
            _adapter?.OnMoodlesCleared(this);
        }

        public void UpdatePrevMoodles()
        {
            prevMoodles.Clear();
            foreach (Transform moodle in moodles)
            {
                var m = moodle.GetComponent<Moodle>();
                if (m != null) prevMoodles.Add(m.type);
            }
        }

        public void AddAllMoodles()
        {
            // REAL 7.0.1 death early-return (decomp line 160: if (!body.alive) return;).
            if (alive)
            {
                foreach (var state in CurrentStates)
                {
                    AddMoodle(state.intensity, state.icon, state.icon, state.icon + "dsc",
                        state.critical, state.chippedOnly);
                }
                if (moodleCount - mainCount > 0)
                {
                    // Bonus "+N" prefab: a child of moodles WITHOUT a Moodle component.
                    var bonus = new GameObject("BonusMoodle");
                    bonus.transform.SetParent(moodles);
                    bonus.transform.GetComponent<RectTransform>().anchoredPosition
                        = new Vector2(mainCount * 70 - 10, 0f);
                }
            }

            // Simulated AddAllMoodlesPostfix (v1.2.2 lowest rebuild boundary). A real
            // Harmony postfix runs after an early return as well.
            _adapter?.OnMoodlesUpdated(this);
        }

        public void AddMoodle(int intensity, string icon, string name, string desc,
            bool critical = false, bool chippedOnly = false)
        {
            // Simulated AddMoodlePrefix.
            _adapter?.OnMoodleAdded(this, intensity, icon, name, desc, critical, chippedOnly);

            if (!chippedOnly || !WorldGeneration.unchipped)
            {
                // REAL 7.0.1 AddMoodle body order (components the mod or sim touch):
                // GameObject+Image -> SetParent -> anchors/position -> Moodle component ->
                // "MoodleInside" child with its own Image -> pop-in (new types only).
                var gameObject = new GameObject("Moodle" + icon);
                gameObject.transform.SetParent(moodles); // appends as LAST sibling
                gameObject.transform.localScale = Vector3.one;
                var rect = gameObject.transform.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                // REAL 7.0.1 creation y: criticals are born ON the wobble sine.
                rect.anchoredPosition = new Vector2(moodleCount * 70,
                    (!critical) ? 0f : (Mathf.Sin(Time.unscaledTime * 6f) * 4f));
                var backgroundImage = gameObject.AddComponent<UnityEngine.UI.Image>();
                backgroundImage.sprite = "background" + intensity;
                var moodle = gameObject.AddComponent<Moodle>();
                moodle.type = icon + intensity.ToString();
                moodle.isSide = sideMoodles;
                var inside = new GameObject("MoodleInside");
                inside.transform.SetParent(gameObject.transform);
                inside.transform.localPosition = Vector3.zero;
                var insideImage = inside.AddComponent<UnityEngine.UI.Image>();
                insideImage.sprite = icon;
                // REAL flash gate: critical && showSideMoodles. During the main block the
                // manager's sideMoodles field is false, so the property is true (main
                // criticals always flash); during the side block it defers to the toggle.
                bool showSideMoodlesNow = !sideMoodles || SideMoodlesVisible;
                if (critical && showSideMoodlesNow)
                {
                    moodle.doWarningFlash = true;
                }
                if (!prevMoodles.Contains(moodle.type))
                {
                    // Pop-in animation start: +75 on y, 2.5 scale, AND (v1.2.3 faithful)
                    // unTransparentTime = 0.5 - the fade-in the game applies via
                    // Moodle.Start()/Update() colors on the NEXT frame.
                    rect.anchoredPosition += Vector2.up * 75f;
                    gameObject.transform.localScale = Vector3.one * 2.5f;
                    moodle.unTransparentTime = 0.5f;
                }
                if (!sideMoodles)
                {
                    mainCount++;
                }
                moodleCount++;
            }

            // Simulated AddMoodlePostfix.
            _adapter?.OnMoodleCreated(this);
        }
    }

    /// <summary>
    /// Faithful Moodle.Update() (real 7.0.1): lerps y toward 0 - or toward the critical
    /// sine wobble - every frame and NEVER touches x. Also lerps scale back toward 1.
    /// </summary>
    public static class MoodleBehaviour
    {
        public static void Update(GameObject moodleObject)
        {
            var m = moodleObject?.GetComponent<Moodle>();
            if (m == null) return;
            var rect = moodleObject.transform.GetComponent<RectTransform>();
            if (rect == null) return;
            // x is deliberately preserved - this is the decompiled behavior the mod relies on.
            // y chases the wobble target when doWarningFlash, else the resting row y (0).
            float targetY = (!m.doWarningFlash) ? 0f : (Mathf.Sin(Time.unscaledTime * 6f) * 4f);
            rect.anchoredPosition = new Vector2(
                rect.anchoredPosition.x,
                Mathf.Lerp(rect.anchoredPosition.y, targetY, Time.unscaledDeltaTime * 12f));
            moodleObject.transform.localScale = Vector3.Lerp(
                moodleObject.transform.localScale, Vector3.one, Time.unscaledDeltaTime * 12f);
        }
    }
