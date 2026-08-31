// Faithful re-implementation of the decompiled Casualties: Unknown MoodleManager rebuild
// cycle (v6.1 ground truth; the 7.0.1 runtime bodies are not publicly available, so the
// game side of the simulator uses the last observed behavior):
//   Update()            : updateTime -= unscaledDeltaTime; when <= 0 -> UpdateMoodles()
//   UpdateMoodles()     : sideMoodles=false; updateTime=0.5; UpdatePrevMoodles();
//                         ClearMoodles(); AddAllMoodles();
//   ClearMoodles()      : Object.Destroy(each child); moodleCount=0; mainCount=0
//   AddAllMoodles()     : hardcoded if/else chain -> AddMoodle(...) per current body state
//                         (+ side moodles, then a bonus "+N" prefab instantiation)
//   AddMoodle(...)      : if (chippedOnly && WorldGeneration.unchipped) return (no-op!);
//                         else new GameObject, SetParent(moodles) [appends LAST sibling],
//                         anchoredPosition = (moodleCount * 70, ...), isSide = sideMoodles,
//                         type = icon + intensity, mainCount/moodleCount++
//   bonus "+N"          : child of moodles WITHOUT a Moodle component at
//                         anchoredPosition = (mainCount * 70 - 10, 0)
//
// The fake game also re-implements Moodle.Update() faithfully: every frame it lerps y
// toward 0 and LEAVES x untouched (so the mod's x writes persist).
//
// Harmony simulation: the fake game calls the exact adapter callbacks that the real
// GamePatches routes, in the same order:
//   per AddMoodle: OnMoodleAdded (prefix) -> [game body] -> OnMoodleCreated (postfix)
//   ClearMoodles : OnMoodlesCleared (postfix)
//   AddAllMoodles: OnMoodlesUpdated (postfix)   [v1.2.2 boundary]
//   UpdateMoodles: OnMoodlesUpdated (postfix)   [same frame; adapter dedupes]
using System;
using System.Collections.Generic;
using HealthAutoArrange.Core;
using HealthAutoArrange.Plugin;
using UnityEngine;

    /// <summary>The game's Moodle component (decompiled fields actually used by the mod).</summary>
    public sealed class Moodle : MonoBehaviour
    {
        public string type;
        public bool isSide;
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

            // Simulated AddAllMoodlesPostfix (v1.2.2 lowest rebuild boundary).
            _adapter?.OnMoodlesUpdated(this);
        }

        public void AddMoodle(int intensity, string icon, string name, string desc,
            bool critical = false, bool chippedOnly = false)
        {
            // Simulated AddMoodlePrefix.
            _adapter?.OnMoodleAdded(this, intensity, icon, name, desc, critical, chippedOnly);

            if (!chippedOnly || !WorldGeneration.unchipped)
            {
                var gameObject = new GameObject("Moodle" + icon);
                gameObject.transform.SetParent(moodles); // appends as LAST sibling
                gameObject.transform.localScale = Vector3.one;
                var rect = gameObject.transform.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.anchoredPosition = new Vector2(moodleCount * 70, 0f);
                var moodle = gameObject.AddComponent<Moodle>();
                moodle.type = icon + intensity.ToString();
                moodle.isSide = sideMoodles;
                if (!prevMoodles.Contains(moodle.type))
                {
                    // Pop-in animation start: +75 on y and 2.5 scale; Moodle.Update lerps
                    // these back toward (row y, scale 1) every frame.
                    rect.anchoredPosition += Vector2.up * 75f;
                    gameObject.transform.localScale = Vector3.one * 2.5f;
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
    /// Faithful Moodle.Update(): lerps y toward 0 (or the critical sine) every frame and
    /// NEVER touches x. Also lerps scale back toward 1. Returns nothing.
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
            rect.anchoredPosition = new Vector2(
                rect.anchoredPosition.x,
                Mathf.Lerp(rect.anchoredPosition.y, 0f, Time.unscaledDeltaTime * 12f));
            moodleObject.transform.localScale = Vector3.Lerp(
                moodleObject.transform.localScale, Vector3.one, Time.unscaledDeltaTime * 12f);
        }
    }

