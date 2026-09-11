// v1.2.2/v1.2.3 behavior simulator - main scenario runner.
//
// "Tests pass is not proof it works": this simulator drives the REAL UnityUiAdapter
// source (compiled in from the plugin project, unchanged) against a faithful fake of
// the REAL 7.0.1 decompiled game rebuild cycle (user-provided runtime DLL, see
// FakeGame.cs provenance), and asserts the mod order at the END OF EVERY FRAME:
//   * after every game UpdateMoodles (rebuild frame, both fake-null semantics),
//   * on steady frames in between,
//   * in BOTH Unity script orders (game update before adapter update, and after),
//   * after piecemeal AddMoodle calls outside the 0.5s cycle,
//   * after external x-displacement attacks (unknown reposition path),
//   * with AddMoodle no-op calls (chippedOnly && unchipped),
//   * with a bonus "+N" child present (side moodles),
//   * with REAL critical-moodle y-wobble active (main-row criticals wobble forever;
//     the mod must never write y or fight the animation),
//   * through the REAL 7.0.1 death early-return (AddAllMoodles returns when
//     body.alive is false; moodles vanish, then come back),
//   * across manager replacement (scene change).
//
// v1.2.3 harness-integrity fix: failures now aggregate into the process exit code.
// (The previous harness never assigned SimTotalFailures, so a failing scenario could
// print FAIL yet still exit 0 - a false-pass verification tool, exactly the kind of
// "flawed process" the v1.2.3 audit was asked to weed out.)
using System;
using System.Collections.Generic;
using System.Linq;
using HealthAutoArrange.Core;
using HealthAutoArrange.Plugin;
using BepInEx.Logging;
using UnityEngine;

internal static class Program
{
    private const float Slot = 70f;

    private sealed class Harness
    {
        public UnityUiAdapter Adapter;
        public MoodleManager Manager;
        public readonly List<string> Failures = new List<string>();
        public readonly List<(LogLevel Level, string Message)> Logs = new List<(LogLevel, string)>();
        /// <summary>Unity script execution order between the BepInEx plugin and the game's
        /// MoodleManager is undefined; the adapter must hold order in both orders.</summary>
        public bool GameUpdateFirst = true;

        public Harness()
        {
            var config = new ArrangeConfig(
                new List<string> { "Critical", "Watch" },
                new Dictionary<string, IReadOnlyList<string>>
                {
                    ["Critical"] = new List<string> { "heartstop*", "braindamage*", "impendingdoom*", "horrified*" },
                    ["Watch"] = new List<string> { "bleeding*", "brokenleg*", "wet*", "adrenaline*" },
                },
                UnknownStatePolicy.Keep,
                new List<ReminderRule>());
            var log = new BepInEx.Logging.ManualLogSource((level, msg) => Logs.Add((level, msg)));
            var dispatcher = new ReminderDispatcher(log);
            var reminders = new ReminderEngine(new List<ReminderRule>());
            Adapter = new UnityUiAdapter(config.CreateSortPlan(), reminders, dispatcher,
                (level, msg) => Logs.Add((level, msg)), null);
            Adapter.Reconfigure(config, enabled: true);
            Manager = new MoodleManager(Adapter);
        }

        /// <summary>Advance one full frame: game update, adapter update, Moodle behaviour,
        /// end-of-frame destroy pass. Script order is controlled by GameUpdateFirst.</summary>
        public void Frame(float unscaledDelta = 1f / 60f, bool runGameUpdate = true)
        {
            Time.unscaledDeltaTime = unscaledDelta;
            Time.deltaTime = unscaledDelta;
            Time.unscaledTime += unscaledDelta;
            Time.realtimeSinceStartup += unscaledDelta;
            Time.frameCount++;

            if (runGameUpdate)
            {
                if (GameUpdateFirst)
                {
                    Manager.Update();
                    Adapter.Update();
                }
                else
                {
                    Adapter.Update();
                    Manager.Update();
                }
            }
            else
            {
                Adapter.Update();
            }

            // Every Moodle's per-frame behaviour (y wobble/lerp only, x preserved).
            for (int i = 0; i < Manager.moodles.childCount; i++)
            {
                var child = Manager.moodles.GetChild(i);
                MoodleBehaviour.Update(child.gameObject);
            }

            SimEngine.EndFrame(); // deferred Destroy pass actually removes ghosts now
        }

        /// <summary>The user-visible order: live moodles sorted by anchoredPosition.x.</summary>
        public List<string> VisualOrder()
        {
            var result = new List<string>();
            for (int i = 0; i < Manager.moodles.childCount; i++)
            {
                var child = Manager.moodles.GetChild(i);
                if (child == null) continue;
                var m = child.GetComponent<Moodle>();
                if (m == null) continue; // bonus "+N" etc.
                if (m.type == null) continue;
                result.Add(m.type);
            }
            return result
                .OrderBy(t => t)
                .ToList() // stable reference order for ties
                .Select(t => (type: t, x: XOf(t)))
                .OrderBy(p => p.x)
                .Select(p => p.type)
                .ToList();
        }

        /// <summary>Live main-row (non-side) moodle x positions - used to prove slots never overlap.</summary>
        public List<float> MainRowX()
        {
            var result = new List<float>();
            for (int i = 0; i < Manager.moodles.childCount; i++)
            {
                var child = Manager.moodles.GetChild(i);
                if (child == null) continue;
                var m = child.GetComponent<Moodle>();
                if (m == null || m.isSide || m.type == null) continue;
                result.Add(child.GetComponent<RectTransform>().anchoredPosition.x);
            }
            return result;
        }

        /// <summary>The critical moodle's y - used to prove the game's wobble keeps running
        /// (the mod must not freeze or fight it by writing y).</summary>
        public float YOf(string type)
        {
            for (int i = 0; i < Manager.moodles.childCount; i++)
            {
                var child = Manager.moodles.GetChild(i);
                var m = child?.GetComponent<Moodle>();
                if (m != null && m.type == type)
                {
                    return child.GetComponent<RectTransform>().anchoredPosition.y;
                }
            }
            throw new InvalidOperationException("type not found: " + type);
        }

        private float XOf(string type)
        {
            for (int i = 0; i < Manager.moodles.childCount; i++)
            {
                var child = Manager.moodles.GetChild(i);
                var m = child?.GetComponent<Moodle>();
                if (m != null && m.type == type)
                {
                    return child.GetComponent<RectTransform>().anchoredPosition.x;
                }
            }
            throw new InvalidOperationException("type not found: " + type);
        }

        public void Assert(string scenario, string message, bool condition, string detail = null)
        {
            if (condition) return;
            // v1.2.3: aggregate globally so the process exit code reflects failures.
            Program.SimTotalFailures++;
            Failures.Add($"[{scenario}] {message}{(detail != null ? " :: " + detail : "")}");
            Console.WriteLine($"FAIL [{scenario}] {message}");
            if (detail != null) Console.WriteLine("      " + detail);
        }
    }

    /// <summary>
    /// Expected mod order for the given live main-row states, given the harness config:
    /// Critical group first (heartstop* > braindamage* > impendingdoom* > horrified*),
    /// then Watch group (bleeding* > brokenleg* > wet* > adrenaline*), unknown states Keep.
    /// </summary>
    private static readonly List<string> CriticalOrder = new List<string>
        { "heartstop", "braindamage", "impendingdoom", "horrified" };
    private static readonly List<string> WatchOrder = new List<string>
        { "bleeding", "brokenleg", "wet", "adrenaline" };

    private static List<string> ExpectedOrder(IEnumerable<string> types)
    {
        var list = types.ToList();
        var result = new List<string>();
        foreach (var wanted in CriticalOrder)
        {
            var hit = list.FirstOrDefault(t => t.StartsWith(wanted, StringComparison.OrdinalIgnoreCase));
            if (hit != null) result.Add(hit);
        }
        foreach (var wanted in WatchOrder)
        {
            var hit = list.FirstOrDefault(t => t.StartsWith(wanted, StringComparison.OrdinalIgnoreCase));
            if (hit != null) result.Add(hit);
        }
        // Unknown states keep their original relative order (Keep policy).
        foreach (var t in list)
        {
            if (!result.Contains(t)) result.Add(t);
        }
        return result;
    }

    private static string Join(IEnumerable<string> items) => string.Join(",", items);

    private static void Main()
    {
        int scenarios = 0;

        foreach (var immediateFakeNull in new[] { true, false })
        {
            var mode = immediateFakeNull ? "immediate-fake-null" : "deferred-fake-null(ghosts)";
            SimEngine.ImmediateFakeNull = immediateFakeNull;

            // ---------------------------------------------------------------
            // Scenario 1: steady 0.5s rebuild cycle with critical-first sort.
            // The game recreates every moodle in ITS source order every 0.5 s; the mod
            // must hold its own order at the end of EVERY frame, including rebuild frames.
            // Runs in BOTH Unity script orders (game-first here, adapter-first in S1b).
            // ---------------------------------------------------------------
            {
                var h = new Harness();
                h.Manager.CurrentStates.AddRange(new[]
                {
                    ("bleeding", 1, false, false),
                    ("wet", 2, false, false),
                    ("heartstop", 3, true, false),
                    ("brokenleg", 1, false, false),
                    ("adrenaline", 0, false, true),
                    ("unknownstate", 1, false, false),
                });
                for (int i = 0; i < 600; i++) // 10 simulated seconds
                {
                    h.Frame();
                    var expected = ExpectedOrder(h.VisualOrder());
                    var actual = h.VisualOrder();
                    h.Assert("S1-" + mode, $"mod order lost on frame {Time.frameCount}",
                        Join(expected) == Join(actual),
                        $"expected {Join(expected)} got {Join(actual)}");
                    if (h.Failures.Count > 0) break;
                }
                scenarios++;
            }

            // ---------------------------------------------------------------
            // Scenario 1b: same steady cycle, but the adapter's Update runs BEFORE the
            // game's MoodleManager.Update each frame (Unity script order is undefined;
            // the watchdog then sees the pre-rebuild hierarchy while the finalize still
            // runs inside the game's own call stack).
            // ---------------------------------------------------------------
            {
                var h = new Harness();
                h.GameUpdateFirst = false;
                h.Manager.CurrentStates.AddRange(new[]
                {
                    ("bleeding", 1, false, false),
                    ("wet", 2, false, false),
                    ("heartstop", 3, true, false),
                    ("brokenleg", 1, false, false),
                    ("adrenaline", 0, false, true),
                    ("unknownstate", 1, false, false),
                });
                for (int i = 0; i < 600; i++)
                {
                    h.Frame();
                    var expected = ExpectedOrder(h.VisualOrder());
                    var actual = h.VisualOrder();
                    h.Assert("S1b-" + mode, $"mod order lost on frame {Time.frameCount} (adapter-first order)",
                        Join(expected) == Join(actual),
                        $"expected {Join(expected)} got {Join(actual)}");
                    if (h.Failures.Count > 0) break;
                }
                scenarios++;
            }

            // ---------------------------------------------------------------
            // Scenario 2: state changes mid-run (player gets hurt; states appear/vanish
            // between rebuilds). Asserts order every frame across 60 rebuild cycles with
            // a scripted state mutation timeline.
            // ---------------------------------------------------------------
            {
                var h = new Harness();
                h.Manager.CurrentStates.AddRange(new[]
                {
                    ("wet", 0, false, false),
                    ("bleeding", 2, false, false),
                });
                var rng = new Random(42);
                for (int cycle = 0; cycle < 60; cycle++)
                {
                    if (cycle % 7 == 0 && !h.Manager.CurrentStates.Any(s => s.icon == "heartstop"))
                    {
                        h.Manager.CurrentStates.Add(("heartstop", 1, true, false));
                    }
                    if (cycle % 11 == 0)
                    {
                        var bleeding = h.Manager.CurrentStates.FirstOrDefault(s => s.icon == "bleeding");
                        if (bleeding.icon != null) h.Manager.CurrentStates.Remove(bleeding);
                    }
                    if (cycle % 13 == 0 && !h.Manager.CurrentStates.Any(s => s.icon == "braindamage"))
                    {
                        h.Manager.CurrentStates.Add(("braindamage", 2, true, true));
                    }
                    // ~35 frames per 0.5s cycle at 1/60.
                    for (int f = 0; f < 35; f++)
                    {
                        h.Frame();
                        var expected = ExpectedOrder(h.VisualOrder());
                        var actual = h.VisualOrder();
                        h.Assert("S2-" + mode, $"mod order lost on frame {Time.frameCount} (cycle {cycle})",
                            Join(expected) == Join(actual),
                            $"expected {Join(expected)} got {Join(actual)}");
                        if (h.Failures.Count > 0) break;
                    }
                    if (h.Failures.Count > 0) break;
                }
                scenarios++;
            }

            // ---------------------------------------------------------------
            // Scenario 3: side moodles + bonus "+N" child (mixed IsSide + non-Moodle
            // child in the container). Main row order must hold; no crash.
            // ---------------------------------------------------------------
            {
                var h = new Harness();
                h.Manager.CurrentStates.AddRange(new[]
                {
                    ("wet", 0, false, false),
                    ("bleeding", 2, false, false),
                });
                for (int i = 0; i < 120; i++)
                {
                    if (i == 30)
                    {
                        // Simulate the game's mid-cycle side moodle block: main states
                        // followed by side states (sideMoodles flips true at line 764 of
                        // the REAL 7.0.1 decompile; our fake exposes the same via UpdateMoodles).
                        h.Manager.sideMoodles = true;
                        h.Manager.CurrentStates.AddRange(new[]
                        {
                            ("adrenaline", 1, false, true),
                            ("unknownside", 0, false, false),
                        });
                        h.Manager.sideMoodles = false;
                    }
                    h.Frame();
                    var all = h.VisualOrder();
                    var expected = ExpectedOrder(all);
                    h.Assert("S3-" + mode, $"main+side order lost on frame {Time.frameCount}",
                        Join(expected) == Join(all),
                        $"expected {Join(expected)} got {Join(all)}");
                    if (h.Failures.Count > 0) break;
                }
                scenarios++;
            }

            // ---------------------------------------------------------------
            // Scenario 4: AddMoodle no-op path (chippedOnly && WorldGeneration.unchipped).
            // The postfix sees NO new child; v1.2.1 would admit a ghost into the fresh set.
            // ---------------------------------------------------------------
            {
                var h = new Harness();
                WorldGeneration.unchipped = true;
                h.Manager.CurrentStates.AddRange(new[]
                {
                    ("wet", 0, false, false),
                    ("braindamage", 2, true, true), // no-op while unchipped
                    ("bleeding", 2, false, false),
                });
                for (int i = 0; i < 240; i++)
                {
                    h.Frame();
                    var all = h.VisualOrder();
                    var expected = ExpectedOrder(all);
                    h.Assert("S4-" + mode, $"order lost with no-op AddMoodle on frame {Time.frameCount}",
                        Join(expected) == Join(all),
                        $"expected {Join(expected)} got {Join(all)}");
                    if (h.Failures.Count > 0) break;
                }
                WorldGeneration.unchipped = false;
                scenarios++;
            }

            // ---------------------------------------------------------------
            // Scenario 5: external x-displacement attack. After the mod order settles, an
            // "unknown game reposition path" (simulating a path the mod did not hook)
            // swaps two moodles' x. The watchdog must restore the order within one frame.
            // ---------------------------------------------------------------
            {
                var h = new Harness();
                h.Manager.CurrentStates.AddRange(new[]
                {
                    ("wet", 0, false, false),
                    ("bleeding", 2, false, false),
                    ("heartstop", 1, true, false),
                });
                for (int i = 0; i < 70; i++) h.Frame();
                {
                    var before = Join(h.VisualOrder());
                    h.Assert("S5-" + mode, "baseline order before attack", before == Join(ExpectedOrder(h.VisualOrder())), before);
                }

                // Attack: swap x of first and last moodle.
                var children = new List<Transform>();
                for (int i = 0; i < h.Manager.moodles.childCount; i++) children.Add(h.Manager.moodles.GetChild(i));
                var moodleChildren = children.Where(c => c?.GetComponent<Moodle>() != null).ToList();
                if (moodleChildren.Count >= 2)
                {
                    var a = moodleChildren[0].GetComponent<RectTransform>();
                    var b = moodleChildren[moodleChildren.Count - 1].GetComponent<RectTransform>();
                    var ax = a.anchoredPosition.x;
                    a.anchoredPosition = new Vector2(b.anchoredPosition.x, a.anchoredPosition.y);
                    b.anchoredPosition = new Vector2(ax, b.anchoredPosition.y);
                }

                // Next frame: adapter Update must run the watchdog and restore order.
                h.Frame();
                h.Assert("S5-" + mode, "watchdog restores order within one frame after external displacement",
                    Join(h.VisualOrder()) == Join(ExpectedOrder(h.VisualOrder())),
                    $"got {Join(h.VisualOrder())} expected {Join(ExpectedOrder(h.VisualOrder()))}");
                // And it must stay restored.
                for (int i = 0; i < 60; i++)
                {
                    h.Frame();
                    h.Assert("S5-" + mode, $"order stays restored on frame {Time.frameCount}",
                        Join(h.VisualOrder()) == Join(ExpectedOrder(h.VisualOrder())));
                    if (h.Failures.Count > 0) break;
                }
                scenarios++;
            }

            // ---------------------------------------------------------------
            // Scenario 6: piecemeal AddMoodle outside the rebuild cycle (no
            // ClearMoodles/AddAllMoodles). Order must hold by the next frame.
            // ---------------------------------------------------------------
            {
                var h = new Harness();
                h.Manager.CurrentStates.AddRange(new[]
                {
                    ("wet", 0, false, false),
                    ("bleeding", 2, false, false),
                });
                for (int i = 0; i < 70; i++) h.Frame();
                {
                    var before = Join(h.VisualOrder());
                    h.Assert("S6-" + mode, "baseline before piecemeal add", before == Join(ExpectedOrder(h.VisualOrder())), before);
                }

                // Piecemeal: the game adds a single moodle directly (game code path that
                // does NOT go through UpdateMoodles; only the AddMoodle patches see it).
                h.Manager.moodleCount = h.Manager.moodles.childCount;
                h.Manager.AddMoodle(3, "heartstop", "heartstop", "dsc", true, false);

                // End of THIS frame the new moodle may be at the game's append position;
                // by the END OF THE NEXT frame it must be in mod order.
                h.Frame();
                h.Frame();
                h.Assert("S6-" + mode, "piecemeal add sorted within two frames",
                    Join(h.VisualOrder()) == Join(ExpectedOrder(h.VisualOrder())),
                    $"got {Join(h.VisualOrder())} expected {Join(ExpectedOrder(h.VisualOrder()))}");
                for (int i = 0; i < 60; i++)
                {
                    h.Frame();
                    h.Assert("S6-" + mode, $"piecemeal order stays on frame {Time.frameCount}",
                        Join(h.VisualOrder()) == Join(ExpectedOrder(h.VisualOrder())));
                    if (h.Failures.Count > 0) break;
                }
                scenarios++;
            }

            // ---------------------------------------------------------------
            // Scenario 7: manager replacement (scene change). A brand-new manager and
            // container appear; the mod must track and sort it.
            // ---------------------------------------------------------------
            {
                var h = new Harness();
                h.Manager.CurrentStates.AddRange(new[]
                {
                    ("wet", 0, false, false),
                    ("bleeding", 2, false, false),
                });
                for (int i = 0; i < 70; i++) h.Frame();

                // Replace the manager (old one destroyed with its hierarchy).
                var oldManager = h.Manager;
                h.Manager = new MoodleManager(h.Adapter);
                h.Manager.CurrentStates.AddRange(new[]
                {
                    ("heartstop", 2, true, false),
                    ("wet", 1, false, false),
                });
                UnityEngine.Object.Destroy(oldManager.gameObject ?? oldManager.transform.gameObject);
                h.Frame(); // destroys settle + new manager rebuilds on its own Update
                for (int i = 0; i < 120; i++)
                {
                    h.Frame();
                    var expected = ExpectedOrder(h.VisualOrder());
                    var actual = h.VisualOrder();
                    h.Assert("S7-" + mode, $"order lost after manager replacement on frame {Time.frameCount}",
                        Join(expected) == Join(actual),
                        $"expected {Join(expected)} got {Join(actual)}");
                    if (h.Failures.Count > 0) break;
                }
                scenarios++;
            }

            // ---------------------------------------------------------------
            // Scenario 8 (v1.2.3, REAL 7.0.1 evidence): critical-moodle y-wobble.
            // The REAL runtime gives every main-row critical moodle doWarningFlash, so
            // Moodle.Update chases a sine on y forever (and a NEW type starts at
            // wobble+75 and decays). The mod must:
            //   (a) hold the x order at the end of every frame,
            //   (b) never overlap slots (distinct x),
            //   (c) leave the wobble running: the critical moodle's y must actually vary
            //       over time (frozen y would mean the mod is writing y and fighting
            //       the game animation - the classic order-fight shape).
            // ---------------------------------------------------------------
            {
                var h = new Harness();
                h.Manager.CurrentStates.AddRange(new[]
                {
                    ("bleeding", 1, false, false),
                    ("wet", 0, false, false),
                    ("heartstop", 3, true, false),   // critical: wobbles forever
                    ("brokenleg", 1, false, false),
                });
                float minWobbleY = float.MaxValue, maxWobbleY = float.MinValue;
                bool sawNewCriticalType = false;
                for (int i = 0; i < 600; i++)
                {
                    if (i == 200)
                    {
                        // A brand-new critical type appears: pop-in (+75 y, 2.5 scale)
                        // stacked ON the wobble base - the harshest y-motion case.
                        h.Manager.CurrentStates.Add(("horrified", 2, true, false));
                        sawNewCriticalType = true;
                    }
                    h.Frame();
                    var all = h.VisualOrder();
                    var expected = ExpectedOrder(all);
                    h.Assert("S8-" + mode, $"wobble: mod order lost on frame {Time.frameCount}",
                        Join(expected) == Join(all),
                        $"expected {Join(expected)} got {Join(all)}");
                    var xs = h.MainRowX();
                    var xsDetail = "x values " + string.Join(";", xs);
                    h.Assert("S8-" + mode, $"wobble: slots overlap on frame {Time.frameCount}",
                        xs.Distinct().Count() == xs.Count,
                        xsDetail);
                    if (h.Failures.Count > 0) break;
                    try
                    {
                        var y = h.YOf("heartstop3");
                        if (y < minWobbleY) minWobbleY = y;
                        if (y > maxWobbleY) maxWobbleY = y;
                    }
                    catch (InvalidOperationException) { /* between rebuild frames */ }
                }
                h.Assert("S8-" + mode, "wobble: critical y never varied (mod froze the animation?)",
                    (maxWobbleY - minWobbleY) > 0.5f,
                    $"y range {minWobbleY:0.###}..{maxWobbleY:0.###}");
                h.Assert("S8-" + mode, "wobble: scenario did not reach the new-critical-type phase",
                    sawNewCriticalType);
                scenarios++;
            }

            // ---------------------------------------------------------------
            // Scenario 9 (v1.2.3, REAL 7.0.1 evidence): death early-return. The real
            // AddAllMoodles starts with `if (!body.alive) return;` - on death every
            // moodle is destroyed and nothing is re-added, while BOTH postfixes still
            // fire (empty fresh set). After revival the order must re-establish within
            // one rebuild cycle and stay stable.
            // ---------------------------------------------------------------
            {
                var h = new Harness();
                h.Manager.CurrentStates.AddRange(new[]
                {
                    ("bleeding", 1, false, false),
                    ("wet", 0, false, false),
                    ("heartstop", 3, true, false),
                });
                for (int i = 0; i < 70; i++)
                {
                    h.Frame();
                    h.Assert("S9-" + mode, $"pre-death order lost on frame {Time.frameCount}",
                        Join(ExpectedOrder(h.VisualOrder())) == Join(h.VisualOrder()));
                    if (h.Failures.Count > 0) break;
                }

                h.Manager.alive = false; // die
                // The death rebuild itself is still gated by the 0.5s timer, so the
                // moodles can legitimately persist for up to ~30 frames before the
                // first alive=false UpdateMoodles clears them. After they vanish they
                // must STAY gone (nothing is re-added while dead).
                bool vanished = false;
                for (int i = 0; i < 120; i++)
                {
                    h.Frame();
                    var orderNow = h.VisualOrder();
                    if (orderNow.Count == 0) { vanished = true; continue; }
                    if (vanished)
                    {
                        h.Assert("S9-" + mode, $"death: moodles reappeared on frame {Time.frameCount}",
                            false, $"visual order: {Join(orderNow)}");
                        break;
                    }
                    // Still pre-death-rebuild: the live moodles must keep their order.
                    h.Assert("S9-" + mode, $"death pending: order lost on frame {Time.frameCount}",
                        Join(ExpectedOrder(orderNow)) == Join(orderNow),
                        $"got {Join(orderNow)} expected {Join(ExpectedOrder(orderNow))}");
                    if (h.Failures.Count > 0) break;
                }
                h.Assert("S9-" + mode, "death: moodles never vanished", vanished);

                // Revive with a different state set (fresh severity mix).
                h.Manager.alive = true;
                h.Manager.CurrentStates.Clear();
                h.Manager.CurrentStates.AddRange(new[]
                {
                    ("brokenleg", 2, false, false),
                    ("heartstop", 1, true, false),
                    ("wet", 0, false, false),
                });
                // One frame for the revive rebuild, one more for settle; then steady.
                h.Frame();
                h.Frame();
                h.Assert("S9-" + mode, "revive: order re-established within two frames",
                    Join(ExpectedOrder(h.VisualOrder())) == Join(h.VisualOrder()),
                    $"got {Join(h.VisualOrder())} expected {Join(ExpectedOrder(h.VisualOrder()))}");
                for (int i = 0; i < 300; i++)
                {
                    h.Frame();
                    h.Assert("S9-" + mode, $"revive: order lost on frame {Time.frameCount}",
                        Join(ExpectedOrder(h.VisualOrder())) == Join(h.VisualOrder()));
                    if (h.Failures.Count > 0) break;
                }
                scenarios++;
            }
        }

        Console.WriteLine($"scenarios run: {scenarios}");
        Console.WriteLine(SimTotalFailures == 0
            ? "ALL SCENARIOS PASS"
            : $"FAILURES: {SimTotalFailures}");
        Environment.Exit(SimTotalFailures == 0 ? 0 : 1);
    }

    private static int SimTotalFailures;
}
