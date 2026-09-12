using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using HealthAutoArrange.Core;
using UnityEngine;

namespace HealthAutoArrange.Plugin
{
    /// <summary>
    /// BepInEx 入口：读取配置、构建适配器、注册 Harmony 补丁、接入 F8 设置窗口。
    /// 排序实际以独立 rules 文件（com.healthautoarrange.plugin.rules.cfg）为准；
    /// BepInEx 的 com.healthautoarrange.plugin.cfg 仅保留默认模板与热键，用于兼容。
    /// 单个可选补丁目标缺失时降级并记录；运行期可捕获的托管异常尽量隔离。
    /// 不宣称能够吞掉 Unity 原生层故障，也不把 ABI/依赖不匹配伪装成“安全可继续”。
    /// </summary>
    [BepInPlugin("com.healthautoarrange.plugin", "Health Auto Arrange", "1.2.3")]
    public class Plugin : BaseUnityPlugin,
        IFallbackSettingsActions,
        IFallbackSettingsStateActions,
        IFallbackSettingsPreviewActions,
        IFallbackSettingsLanguageActions,
        IFallbackSettingsPersistenceActions,
        IFallbackSettingsUpdateActions
    {
        internal static ManualLogSource PluginLog;
        internal static UnityUiAdapter Adapter;

        /// <summary>F8 设置窗口是否打开（供输入拦截补丁查询）。</summary>
        internal static bool SettingsWindowOpen => SettingsWindow != null && SettingsWindow.IsOpen;

        private const string RulesFileName = "com.healthautoarrange.plugin.rules.cfg";

        private Harmony _harmony;
        internal static FallbackSettingsWindow SettingsWindow;
        private UiConfigModel _uiModel;
        private ReminderPresentation _presentation;
        private TransparentReminderOverlay _overlay;
        private string _rulesPath;
        private ConfigEntry<KeyCode> _settingsKey;
        private ConfigEntry<KeyCode> _debugDumpKey;
        private ConfigEntry<string> _uiLanguage;
        private ConfigEntry<bool> _autoCheckUpdates;
        private ConfigEntry<float> _updateCheckDelaySeconds;
        private ConfigEntry<string> _officialManifestUrl;
        private ConfigEntry<string> _mirrorManifestUrl;  // v1.2.2: fallback for networks that cannot reach raw.githubusercontent.com
        private SafeUpdater _updater;
        private string _pendingUpdateVersion;
        private string _statusMessage = string.Empty;
        private const float BadgeDesignHeight = 1080f;  // v1.2.3 DPI: matches FallbackSettingsWindow/TransparentReminderOverlay
        private float _statusMessageUntil;

        private void Awake()
        {
            PluginLog = Logger;
            try
            {
                InitializePlugin();
            }
            catch (Exception ex)
            {
                // A partially initialized BaseUnityPlugin may still receive Update/OnGUI callbacks.
                // If initialization fails after creating state or applying one of our Harmony patches,
                // fail closed: remove our patches, clear static entry points, and disable this component.
                try { Logger?.LogError($"HealthAutoArrange initialization failed; plugin disabled: {ex}"); } catch { }
                try { _harmony?.UnpatchSelf(); } catch (Exception cleanupEx)
                {
                    try { Logger?.LogWarning($"HealthAutoArrange initialization cleanup failed: {cleanupEx.Message}"); } catch { }
                }
                Adapter = null;
                SettingsWindow = null;
                PluginLog = null;
                _overlay = null;
                _presentation = null;
                _harmony = null;
                _updater = null;
                enabled = false;
            }
        }

        private void InitializePlugin()
        {
            Logger.LogInfo($"HealthAutoArrange.Plugin loading: {Info.Metadata.Name} v{Info.Metadata.Version}");
            Logger.LogInfo($"Runtime: Unity={Application.unityVersion}, Game={Application.version}, Assembly-CSharp={typeof(MoodleManager).Assembly.GetName().Version}");

            // 1. 绑定热键（Debug 段；F8 设置窗口、F9 诊断 dump）
            _settingsKey = Config.Bind("Debug", "SettingsKey", KeyCode.F8,
                "Key to open/close the settings window.");
            _debugDumpKey = Config.Bind("Debug", "DebugDumpKey", KeyCode.F9,
                "Key to dump current Moodle diagnostics to the log.");
            _uiLanguage = Config.Bind("UI", "Language", "Auto",
                "Settings GUI language: Auto, Chinese, or English. The in-game button writes Chinese/English here.");
            _autoCheckUpdates = Config.Bind("Updates", "AutoCheck", true,
                "Check GitHub once shortly after each game launch and show a notification when an update is available.");
            _updateCheckDelaySeconds = Config.Bind("Updates", "CheckDelaySeconds", 10f,
                "Seconds to wait after game launch before the one-time GitHub update notification check.");
            _officialManifestUrl = Config.Bind("Updates", "OfficialManifestUrl",
                "https://raw.githubusercontent.com/purrfecto114-lgtm/HealthAutoArrange/update-dist/latest.txt",
                "Primary update manifest. Hosts allowed: github.com, raw.githubusercontent.com, cdn.jsdelivr.net, fastly.jsdelivr.net, gcore.jsdelivr.net.");
            _mirrorManifestUrl = Config.Bind("Updates", "MirrorManifestUrl",
                "https://cdn.jsdelivr.net/gh/purrfecto114-lgtm/HealthAutoArrange@update-dist/latest.txt",
                "v1.2.2: fallback manifest mirror (jsDelivr CDN) used automatically when the primary URL is unreachable, e.g. on networks where raw.githubusercontent.com is blocked.");
            var chineseUi = ResolveChineseUiLanguage();

            // 2. 读取 BepInEx 配置（默认模板 + 兼容解析）
            var parseResult = LoadConfig();
            foreach (var warning in parseResult.Warnings)
            {
                Logger.LogWarning($"Config: {warning}");
            }

            // 3. 构建适配器：排序核心 + 提醒引擎 + 分发器 + 透明提醒展示
            _presentation = new ReminderPresentation();
            _overlay = new TransparentReminderOverlay(_presentation, chineseUi);
            var dispatcher = new ReminderDispatcher(Logger);
            var reminders = new ReminderEngine(parseResult.Config.Reminders);
            Adapter = new UnityUiAdapter(
                parseResult.Config.CreateSortPlan(),
                reminders,
                dispatcher,
                (level, msg) => Logger.Log(level, msg),
                OnReminderMessage);

            // 4. rules 文件：首次无文件时从 ConfigFile 解析模型并写入；否则读取并应用。
            _rulesPath = Path.Combine(BepInEx.Paths.ConfigPath, RulesFileName);
            Logger.LogInfo($"Rules file: {_rulesPath}");
            _uiModel = LoadRulesModel(parseResult.Config);
            ApplyModel(_uiModel);
            Logger.LogInfo($"In-group ordering: {UnityUiAdapter.DescribeInGroupSort(parseResult.Config)} [InGroupSort={parseResult.Config.InGroupSortMode}].");

            // 5. 启动更新检查：仅从 GitHub 读取签名清单并提醒，不下载或安装文件。
            _updater = new SafeUpdater(
                Logger,
                Info.Metadata.Version.ToString(),
                () => _officialManifestUrl?.Value,
                () => _mirrorManifestUrl?.Value,  // v1.2.2: automatic jsDelivr fallback
                ShowUpdateAvailableFeedback);

            // 6. F8 设置窗口使用独立编辑副本，未保存修改不会污染宿主的已应用模型。
            SettingsWindow = new FallbackSettingsWindow(_uiModel.Clone(), this, RefreshStateCatalog(), chineseUi);
            if (_autoCheckUpdates != null && _autoCheckUpdates.Value) StartCoroutine(_updater.AutoCheckAfterDelay(Mathf.Max(0f, _updateCheckDelaySeconds?.Value ?? 10f)));

            // 7. Harmony 补丁：目标缺失时降级，不抛异常
            _harmony = new Harmony("com.healthautoarrange.plugin");
            try
            {
                // v1.2.2: patch EVERY rebuild boundary instead of one preferred hook.
                // Patch only the reverse-engineered parameterless signatures (name + empty
                // formal types; a name-only lookup can become ambiguous or silently select a
                // new overload after a game update).
                //   - UpdateMoodles: the 0.5s timer path (v1.1.x hook).
                //   - AddAllMoodles: the LOWEST boundary every rebuild funnels through
                //     regardless of caller. v1.2.1 left any non-UpdateMoodles caller
                //     unsupervised, so the game's source order rendered until the 4 Hz net
                //     corrected it (user-visible "two orders taking turns").
                //   - ClearMoodles: start-of-cycle reset for the per-cycle accumulation.
                var boundaryNames = new[]
                {
                    new { Patch = nameof(GamePatches.MoodleRefreshPostfix), Target = "UpdateMoodles", Label = "UpdateMoodles (0.5s timer path; postfix dedupes against AddAllMoodles same-frame finalize)" },
                    new { Patch = nameof(GamePatches.AddAllMoodlesPostfix), Target = "AddAllMoodles", Label = "AddAllMoodles (lowest rebuild boundary; same-frame finalize)" },
                    new { Patch = nameof(GamePatches.ClearMoodlesPostfix), Target = "ClearMoodles", Label = "ClearMoodles (cycle reset + ghost-hide, v1.2.3)" }
                };
                int boundariesPatched = 0;
                foreach (var boundary in boundaryNames)
                {
                    var method = AccessTools.Method(typeof(MoodleManager), boundary.Target, Type.EmptyTypes);
                    if (method == null)
                    {
                        Logger.LogWarning($"MoodleManager.{boundary.Target} not found; boundary hook skipped ({boundary.Label}).");
                        continue;
                    }
                    _harmony.Patch(method, postfix: new HarmonyMethod(typeof(GamePatches), boundary.Patch));
                    boundariesPatched++;
                    Logger.LogInfo($"Patched Moodle rebuild boundary: {boundary.Label}.");
                }
                if (boundariesPatched == 0)
                {
                    Logger.LogWarning("No Moodle rebuild boundary could be patched; sorting refresh hooks disabled.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to patch Moodle rebuild boundaries: {ex.Message}");
            }

            // AddMoodle 前缀只捕获元数据；后缀（v1.2.0 新增）记录新创建 moodle 的
            // instance id 到 fresh-set。刷新边界上面只选择一个方法，避免重复触发。
            try
            {
                var addMoodle = AccessTools.Method(
                    typeof(MoodleManager),
                    "AddMoodle",
                    new[] { typeof(int), typeof(string), typeof(string), typeof(string), typeof(bool), typeof(bool) });
                if (addMoodle == null)
                {
                    Logger.LogWarning("MoodleManager.AddMoodle(int,string,string,string,bool,bool) not found; capture disabled.");
                }
                else
                {
                    _harmony.Patch(
                        addMoodle,
                        prefix: new HarmonyMethod(typeof(GamePatches), nameof(GamePatches.AddMoodlePrefix)),
                        postfix: new HarmonyMethod(typeof(GamePatches), nameof(GamePatches.AddMoodlePostfix)));
                    Logger.LogInfo("Patched MoodleManager.AddMoodle capture (prefix) + fresh-set tracker/creation-time positioning/pre-fade (postfix).");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to patch MoodleManager.AddMoodle: {ex.Message}");
            }

            // UIUtil.IsPointerOverUIElement() 无参版：F8 设置窗口打开时视为指针在 UI 上，
            // 拦截游戏攻击/交互输入（IMGUI 窗口不在 EventSystem 中，原生检测永远 false）。
            try
            {
                var pointerOverUi = AccessTools.Method(typeof(UIUtil), "IsPointerOverUIElement", Type.EmptyTypes);
                if (pointerOverUi == null)
                {
                    Logger.LogWarning("UIUtil.IsPointerOverUIElement() not found; settings input blocking disabled.");
                }
                else
                {
                    var pointerPostfix = new HarmonyMethod(
                        typeof(GamePatches), nameof(GamePatches.IsPointerOverUIElementPostfix))
                    {
                        priority = Priority.Last
                    };
                    _harmony.Patch(pointerOverUi, postfix: pointerPostfix);
                    Logger.LogInfo("Patched UIUtil.IsPointerOverUIElement input blocking (composing postfix).");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to patch UIUtil.IsPointerOverUIElement: {ex.Message}");
            }

            Logger.LogInfo("HealthAutoArrange.Plugin loaded.");
        }

        /// <summary>
        /// 每帧：先处理热键（F8 设置、F9 诊断、Ctrl+R 立即重排、Ctrl+E 快速开关），
        /// 再驱动 Adapter。把热键检查放在 Adapter.Update() 之前是为了即使 adapter 抛
        /// 出未捕获异常时，F8/F9 仍然可用——这是历史上"GUI 失效"反馈的根因之一。
        /// </summary>
        private void Update()
        {
            // Hotkey checks FIRST so input is never swallowed by adapter exceptions.
            // Each block has its own try/catch so a failure in one path cannot disable the others.
            try
            {
                if (_settingsKey != null && Input.GetKeyDown(_settingsKey.Value))
                {
                    ToggleSettingsWindow();
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: settings key toggle error: {ex.Message}");
            }

            try
            {
                if (_debugDumpKey != null && Input.GetKeyDown(_debugDumpKey.Value))
                {
                    Logger.LogInfo("F9 diagnostics requested.");
                    DumpDiagnostics();
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: diagnostics key error: {ex.Message}");
            }

            // Ctrl+R: force-resort fallback. Useful when F8 GUI is broken or when user
            // wants to verify the sort pipeline works without opening the settings window.
            try
            {
                if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.R))
                {
                    Logger.LogInfo("Ctrl+R force-resort requested.");
                    ForceResort();
                    _statusMessage = "HealthAutoArrange: force resort requested";
                    _statusMessageUntil = Time.realtimeSinceStartup + 3f;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: force-resort hotkey error: {ex.Message}");
            }

            // Ctrl+E: quick enable/disable toggle. Lets users toggle the auto-arrange without
            // opening the F8 window. The new state is applied immediately via ApplyModel.
            try
            {
                if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.E))
                {
                    if (_uiModel != null)
                    {
                        _uiModel.Enabled = !_uiModel.Enabled;
                        ApplyModel(_uiModel);
                        try { Config.Save(); } catch { /* ignore */ }
                        Logger.LogInfo($"Ctrl+E toggled auto-arrange: Enabled={_uiModel.Enabled}");
                        _statusMessage = "HealthAutoArrange: " + (_uiModel.Enabled ? "ENABLED" : "DISABLED");
                        _statusMessageUntil = Time.realtimeSinceStartup + 3f;
                        // Reflect the change into the F8 window's editable model too, so the
                        // checkbox stays in sync when the user opens the window later.
                        SettingsWindow?.SyncEnabledFromRuntime(_uiModel.Enabled);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: quick-toggle hotkey error: {ex.Message}");
            }

            try
            {
                Adapter?.Update();
                TryShowPendingUpdateFeedback();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: Update error: {ex.Message}");
            }
        }

        /// <summary>
        /// IMGUI 设置窗口绘制 + 透明提醒 overlay 绘制 + 状态徽标绘制。
        /// 两者分别捕获可恢复的托管异常；overlay 不依赖 F8 窗口是否打开。
        /// 状态徽标独立于 F8 窗口，让用户随时确认插件已加载并查看当前 Enabled 状态。
        /// </summary>
        private void OnGUI()
        {
            // Status badge is drawn first so it's always visible even if F8 window throws.
            try
            {
                DrawStatusBadge();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: status badge GUI error: {ex.Message}");
            }

            try
            {
                SettingsWindow?.Draw();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: settings window GUI error: {ex.Message}");
            }

            try
            {
                _overlay?.Draw();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: reminder overlay GUI error: {ex.Message}");
            }
        }

        /// <summary>
        /// 在屏幕左上角绘制一个小的状态徽标。当 F8 设置窗口打开时，徽标会移到右上角，
        /// 避免与设置窗口的左上角内容冲突。徽标颜色和文字反映当前 Enabled 状态。
        /// 这是"GUI 失效"反馈的兜底——即使 F8 窗口自身因任何原因无法打开，用户仍能
        /// 通过徽标确认插件加载情况，并通过 Ctrl+E / Ctrl+R / F9 等热键操作。
        /// </summary>
        private void DrawStatusBadge()
        {
            if (_uiModel == null) return;
            var previousColor = GUI.color;
            var previousMatrix = GUI.matrix;
            try
            {
                var enabled = _uiModel.Enabled;
                var badgeText = "HAA: " + (enabled ? "ON" : "OFF")
                    + (SettingsWindowOpen ? " (F8)" : string.Empty);
                if (!string.IsNullOrEmpty(_statusMessage)
                    && Time.realtimeSinceStartup < _statusMessageUntil)
                {
                    badgeText += "\n" + _statusMessage;
                }
                var content = new GUIContent(badgeText);
                var style = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                style.normal.textColor = Color.white;
                var size = style.CalcSize(content);
                var padding = 6f;
                var w = size.x + padding * 2f;
                var h = size.y + padding * 2f;
                // v1.2.3 DPI: apply the SAME 1x-2x scaling (Screen.height / 1080) as the
                // F8 settings window and the reminder overlay, so the badge (and the
                // transient hotkey status messages it shows) stay readable on 1440p/4K.
                // Without this the badge was the only unscaled IMGUI element: at 4K it
                // rendered at half the relative size of every other mod UI. Badge coords
                // are now virtual (pre-matrix) pixels; IMGUI transforms input to match.
                var scale = Screen.height > 0 ? Mathf.Clamp(Screen.height / BadgeDesignHeight, 1f, 2f) : 1f;
                GUI.matrix = previousMatrix * Matrix4x4.Scale(new Vector3(scale, scale, 1f));
                var virtualWidth = Screen.width > 0 ? Screen.width / scale : 1280f;
                // Move the badge to the top-right when F8 window is open, top-left otherwise.
                var x = SettingsWindowOpen ? virtualWidth - w - 8f : 8f;
                var y = 8f;
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.Box(new Rect(x, y, w, h), GUIContent.none, style);
                GUI.color = enabled ? new Color(0.55f, 1f, 0.55f, 1f) : new Color(1f, 0.55f, 0.55f, 1f);
                GUI.Label(new Rect(x, y, w, h), badgeText, style);
            }
            finally
            {
                GUI.color = previousColor;
                GUI.matrix = previousMatrix;
            }
        }

        private void ToggleSettingsWindow()
        {
            try
            {
                if (SettingsWindow == null) return;
                if (SettingsWindow.IsOpen) SettingsWindow.Close();
                else SettingsWindow.Open();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: settings window toggle error: {ex.Message}");
            }
        }

        // ---- IFallbackSettingsActions ----

        /// <summary>
        /// Save：Normalize → 原子写 rules 文件 → 应用模型。磁盘失败时可继续应用到本次会话，
        /// 但通过 SettingsSaveResult 明确告诉 UI 不要清除“未保存”状态。
        /// </summary>
        public SettingsSaveResult SaveWithResult(UiConfigModel model)
        {
            if (model == null) return new SettingsSaveResult(false, false, "Model is null.");
            try
            {
                model.Normalize();

                // Apply first. Never persist a rules file that the current runtime rejected:
                // doing so can make the next launch fail with settings the user never actually saw working.
                if (!ApplyModel(model))
                {
                    const string applyDetail = "Runtime rejected the edited settings; the rules file was left unchanged.";
                    Logger.LogWarning("HealthAutoArrange: " + applyDetail);
                    return new SettingsSaveResult(false, false, applyDetail);
                }

                _uiModel = model.Clone();
                try
                {
                    RulesFileStore.Write(_rulesPath, model);
                    Logger.LogInfo("Settings saved and applied.");
                    return new SettingsSaveResult(true, true, string.Empty);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Failed to write rules file: {ex.Message}");
                    Logger.LogInfo("Settings applied in memory, but the rules file was not saved.");
                    return new SettingsSaveResult(true, false, ex.Message);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: Save failed: {ex.Message}");
                return new SettingsSaveResult(false, false, ex.Message);
            }
        }

        public void Save(UiConfigModel model)
        {
            SaveWithResult(model);
        }

        /// <summary>
        /// Reload：读取 rules 文件并应用；返回模型供窗口刷新。
        /// </summary>
        public UiConfigModel Reload()
        {
            try
            {
                var model = RulesFileStore.Read(_rulesPath);
                if (model == null)
                {
                    Logger.LogWarning("Rules file not found; reload aborted.");
                    return null;
                }
                if (!ApplyModel(model))
                {
                    Logger.LogWarning("Rules file was read but could not be applied; reload aborted.");
                    return null;
                }
                _uiModel = model.Clone();
                Logger.LogInfo("Settings reloaded from rules file.");
                return model.Clone();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: Reload failed: {ex.Message}");
                return null;
            }
        }

        public void ForceResort()
        {
            try
            {
                Adapter?.ForceResort();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: ForceResort failed: {ex.Message}");
            }
        }

        public void DumpDiagnostics()
        {
            try
            {
                Adapter?.DumpDiagnostics();
                DumpPatchDiagnostics();
                ShowDiagnosticsFeedback();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: Diagnostics failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 将各 Harmony 补丁的运行期触发情况写入日志。当用户反馈"功能失效"时，
        /// 这一段可以让用户/作者快速判断到底是补丁没装上，还是装上了但没被调用，
        /// 还是调用了但 Adapter 内部失败。三档区分能极大缩短问题定位时间。
        /// </summary>
        private void DumpPatchDiagnostics()
        {
            try
            {
                Logger.LogInfo("----- HealthAutoArrange patch diagnostics -----");
                Logger.LogInfo($"  MoodleRefreshPostfix invoked: {GamePatches.MoodleRefreshPostfixInvoked} (count: {GamePatches.MoodleRefreshInvokeCount})");
                Logger.LogInfo($"  AddMoodlePrefix      invoked: {GamePatches.AddMoodlePrefixInvoked} (count: {GamePatches.AddMoodleInvokeCount})");
                Logger.LogInfo($"  AddMoodlePostfix     invoked: {GamePatches.AddMoodlePostfixInvoked} (count: {GamePatches.AddMoodlePostfixInvokeCount})");
                Logger.LogInfo($"  AddAllMoodlesPostfix invoked: {GamePatches.AddAllMoodlesPostfixInvoked} (count: {GamePatches.AddAllMoodlesInvokeCount})");
                Logger.LogInfo($"  ClearMoodlesPostfix  invoked: {GamePatches.ClearMoodlesPostfixInvoked} (count: {GamePatches.ClearMoodlesInvokeCount})");
                Logger.LogInfo($"  IsPointerOverUIElementPostfix invoked: {GamePatches.PointerOverUiPostfixInvoked}");
                Logger.LogInfo($"  Settings window open now: {SettingsWindowOpen}");
                Logger.LogInfo($"  Auto-arrange Enabled: {(_uiModel?.Enabled ?? false)}");
                Logger.LogInfo($"  In-group ordering: {UnityUiAdapter.DescribeInGroupSort(_uiModel?.ToConfig())} [InGroupSort={_uiModel?.InGroupSortMode}]");
                Logger.LogInfo($"  Adapter alive: {(Adapter != null ? "yes" : "no")}");
                Logger.LogInfo($"  Manager tracked: {(Adapter != null ? "yes" : "no")}");
                Logger.LogInfo("----- end patch diagnostics -----");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: patch diagnostics dump failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 非侵入式反馈：优先 Logger；PlayerCamera.main 可用时追加 DoAlert。
        /// F9 与 F8 窗口的 Diagnostics 按钮共用此路径。失败仅记录，不影响游戏。
        /// </summary>
        private void ShowDiagnosticsFeedback()
        {
            try
            {
                var text = _overlay != null ? _overlay.Text.DiagnosticsWritten : "HealthAutoArrange diagnostics written to LogOutput.log";
                Logger.LogInfo(text);
                var camera = PlayerCamera.main;
                if (camera != null)
                {
                    camera.DoAlert(text, false);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: diagnostics feedback failed: {ex.Message}");
            }
        }

        /// <summary>Close：无副作用（窗口自身已关闭）。</summary>
        public void Close()
        {
            // 无副作用。
        }

        // ---- IFallbackSettingsUpdateActions ----

        public UpdateUiSnapshot GetUpdateStatus()
        {
            return _updater?.Snapshot ?? new UpdateUiSnapshot(
                UpdateUiState.Idle, Info?.Metadata?.Version?.ToString() ?? "1.1.8", string.Empty, string.Empty, string.Empty);
        }

        public void OpenUpdatePage()
        {
            try { _updater?.OpenReleasePage(); }
            catch (Exception ex) { Logger.LogWarning($"HealthAutoArrange updater release page failed: {ex.Message}"); }
        }

        public bool AutoCheckUpdates
        {
            get => _autoCheckUpdates != null && _autoCheckUpdates.Value;
            set
            {
                if (_autoCheckUpdates == null || _autoCheckUpdates.Value == value) return;
                _autoCheckUpdates.Value = value;
                try { Config.Save(); } catch (Exception ex) { Logger.LogWarning($"Could not persist updater AutoCheck: {ex.Message}"); }
            }
        }

        private void ShowUpdateAvailableFeedback(string version)
        {
            try
            {
                var text = (_overlay != null ? _overlay.Text.UpdateAvailableReminder(version) : "HealthAutoArrange update " + version + " is available on GitHub (F8 for details).");
                Logger.LogInfo(text);
                _pendingUpdateVersion = version;
                TryShowPendingUpdateFeedback();
            }
            catch (Exception ex) { Logger.LogWarning($"HealthAutoArrange update reminder failed: {ex.Message}"); }
        }

        private void TryShowPendingUpdateFeedback()
        {
            if (string.IsNullOrEmpty(_pendingUpdateVersion)) return;
            var camera = PlayerCamera.main;
            if (camera == null) return;
            var text = _overlay != null ? _overlay.Text.UpdateAvailableReminder(_pendingUpdateVersion)
                : "HealthAutoArrange update " + _pendingUpdateVersion + " is available on GitHub (F8 for details).";
            camera.DoAlert(text, false);
            _pendingUpdateVersion = null;
        }

        // ---- IFallbackSettingsStateActions / IFallbackSettingsPreviewActions ----

        /// <summary>
        /// 从适配器实际扫描到的 Moodle 节点构建状态目录；AddMoodle 捕获仅用于补充元数据。
        /// </summary>
        public IReadOnlyList<StateCatalogEntry> RefreshStateCatalog()
        {
            try
            {
                var observed = Adapter?.RefreshObservedStates();
                return observed == null ? new List<StateCatalogEntry>() : observed.ToList();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: RefreshStateCatalog failed: {ex.Message}");
                return new List<StateCatalogEntry>();
            }
        }

        /// <summary>
        /// 预览提醒：立即入队展示，不受正式提醒冷却限制。
        /// 显示名/强度优先取状态目录中的匹配项；无匹配时回退本地化“状态提醒预览”文案。
        /// </summary>
        public void PreviewReminder(UiReminderModel model)
        {
            try
            {
                if (model == null) return;
                var reminderBaseId = MoodleIdentity.PatternBaseId(model.Name);
                var entry = RefreshStateCatalog().FirstOrDefault(e => e != null
                    && string.Equals(e.BaseId, reminderBaseId, StringComparison.OrdinalIgnoreCase));

                var fallback = _overlay != null ? _overlay.Text.PreviewFallback : "State reminder preview";
                var displayName = entry != null && !string.IsNullOrWhiteSpace(entry.DisplayName)
                    ? entry.DisplayName : fallback;
                var intensity = entry != null && entry.Intensities != null && entry.Intensities.Count > 0
                    ? entry.Intensities[entry.Intensities.Count - 1] : -1;
                var runtimeId = entry != null && !string.IsNullOrWhiteSpace(entry.LastRuntimeId)
                    ? entry.LastRuntimeId : model.Name;
                var baseId = entry != null ? entry.BaseId : MoodleIdentity.PatternBaseId(model.Name);

                var context = new ReminderRenderContext(runtimeId, displayName, string.Empty, intensity, baseId);
                _presentation?.Preview(context, DateTimeOffset.UtcNow,
                    ReminderVisualPresetBuilder.Build(model), model.Template);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: PreviewReminder failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Formal reminder callback. ReminderEngine is the single source of truth for send cadence.
        /// Only BottomAlert produces the transparent on-screen overlay; Log remains log-only and the
        /// legacy HealthPanelHint stays non-visual. Presentation no longer applies a second long
        /// cooldown, so a state that disappears and legitimately reappears can alert immediately.
        /// </summary>
        private void OnReminderMessage(ReminderMessage message, ReminderRenderContext context)
        {
            try
            {
                if (message == null || message.Mode != ReminderMode.BottomAlert) return;

                var model = _uiModel?.Reminders?.FirstOrDefault(r => r != null
                    && string.Equals(r.Name, message.RuleName, StringComparison.OrdinalIgnoreCase));
                var preset = model != null ? ReminderVisualPresetBuilder.Build(model) : null;
                var template = model != null && !string.IsNullOrWhiteSpace(model.Template)
                    ? model.Template : null;

                // No schedule-level dedupe here: the engine already emitted one authoritative event.
                _presentation?.Enqueue(message, context, DateTimeOffset.UtcNow, 0d, preset, template);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: OnReminderMessage failed: {ex.Message}");
            }
        }

        public void SetChineseUi(bool chinese)
        {
            try
            {
                if (_uiLanguage != null)
                {
                    _uiLanguage.Value = chinese ? "Chinese" : "English";
                    Config.Save();
                }
                _overlay?.SetLanguage(chinese);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: failed to persist GUI language: {ex.Message}");
            }
        }

        private bool ResolveChineseUiLanguage()
        {
            var value = (_uiLanguage?.Value ?? "Auto").Trim();
            if (value.Equals("Chinese", StringComparison.OrdinalIgnoreCase)
                || value.Equals("zh", StringComparison.OrdinalIgnoreCase)
                || value.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
                || value.Equals("中文", StringComparison.OrdinalIgnoreCase)) return true;
            if (value.Equals("English", StringComparison.OrdinalIgnoreCase)
                || value.Equals("en", StringComparison.OrdinalIgnoreCase)) return false;
            return Application.systemLanguage.ToString().StartsWith("Chinese", StringComparison.OrdinalIgnoreCase);
        }

        // ---- 配置加载 ----

        /// <summary>
        /// 加载 rules 模型：优先读取 rules 文件；首次无文件时从 ConfigFile 解析模型并写入。
        /// </summary>
        private UiConfigModel LoadRulesModel(ArrangeConfig fallbackConfig)
        {
            try
            {
                var existing = RulesFileStore.Read(_rulesPath);
                if (existing != null)
                {
                    Logger.LogInfo("Loaded settings from rules file.");
                    return existing;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to read rules file: {ex.Message}");
            }

            var initial = UiConfigModel.FromConfig(fallbackConfig, true);
            try
            {
                RulesFileStore.Write(_rulesPath, initial);
                Logger.LogInfo("Created rules file from ConfigFile template.");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to create rules file: {ex.Message}");
            }
            return initial;
        }

        /// <summary>
        /// 应用模型：更新 ArrangeConfig，并经 Adapter.Reconfigure 增量更新提醒规则。
        /// model.Enabled 只控制状态图标排序；提醒规则按各自 Enabled 独立运行。
        /// 保持当前 manager、观察快照与捕获注册表。
        /// </summary>
        private bool ApplyModel(UiConfigModel model)
        {
            try
            {
                if (model == null) return false;
                var config = model.ToConfig();
                if (Adapter == null) return false;
                Adapter.Reconfigure(config, model.Enabled);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: ApplyModel failed: {ex.Message}");
                return false;
            }
        }

        private ConfigParseResult LoadConfig()
        {
            // 绑定可编辑默认模板（仅示例，非逻辑依赖参数）。
            BindDefaultTemplate();

            // 将 BepInEx ConfigFile 中的全部条目（含用户新增的 Group.<name>.States、
            // Reminder.<rule>.* 等自定义键）序列化为纯键值文本，交由纯 C# 解析器统一处理。
            var sb = new StringBuilder();
            foreach (var kv in Config)
            {
                sb.Append(kv.Key.Key).Append(" = ").Append(kv.Value.BoxedValue).AppendLine();
            }
            return ConfigTextParser.Parse(sb.ToString());
        }

        /// <summary>
        /// 绑定可编辑默认模板。这些键仅用于首次运行生成配置文件，作为示例供用户参考；
        /// 用户可自由增删/修改任意 Group.&lt;name&gt;.States 与 Reminder.&lt;rule&gt;.* 键。
        /// 核心排序/分组/提醒逻辑完全数据驱动，不依赖这些示例值；
        /// 空配置（无分组、无提醒）同样正常工作。
        /// </summary>
        private void BindDefaultTemplate()
        {
            Config.Bind("General", "GroupOrder", "Priority 1, Priority 2",
                "Comma-separated group order. The starter groups are intentionally empty; assign states observed in-game from the F8 window.");
            Config.Bind("General", "UnknownStatePolicy", "Keep",
                "Unknown state policy: Keep (recommended; preserve position) or End (move unknown states to end).");
            Config.Bind("General", "InGroupSort", "IntensityDesc",
                "v1.2.3: in-group ordering. IntensityDesc = order by current effect strength, strongest first (default); IntensityAsc = weakest first; RuleIndex = order states as declared per group. Rules-file key: InGroupSort.");
            Config.Bind("Groups", "Group.Priority 1.States", string.Empty,
                "Highest-priority observed Moodle patterns. Prefer assigning them from the in-game state catalog.");
            Config.Bind("Groups", "Group.Priority 2.States", string.Empty,
                "Lower-priority observed Moodle patterns. Prefer assigning them from the in-game state catalog.");
        }

        private void OnDestroy()
        {
            try
            {
                _harmony?.UnpatchSelf();
            }
            catch (Exception ex)
            {
                Logger?.LogWarning($"HealthAutoArrange: cleanup unpatch failed: {ex.Message}");
            }
            finally
            {
                // BepInEx normally keeps plugins loaded for the process lifetime, but clearing
                // static references makes scene/plugin teardown and developer hot-reload safer.
                Adapter = null;
                SettingsWindow = null;
                PluginLog = null;
                _overlay = null;
                _presentation = null;
                _harmony = null;
                _updater = null;
            }
        }
    }
}
