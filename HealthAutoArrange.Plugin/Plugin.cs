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
    /// 补丁目标缺失或任何初始化失败时仅记录警告，不阻塞游戏启动。
    /// </summary>
    [BepInPlugin("com.healthautoarrange.plugin", "Health Auto Arrange", "1.1.1")]
    public class Plugin : BaseUnityPlugin,
        IFallbackSettingsActions,
        IFallbackSettingsStateActions,
        IFallbackSettingsPreviewActions
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

        private void Awake()
        {
            PluginLog = Logger;
            Logger.LogInfo($"HealthAutoArrange.Plugin loading: {Info.Metadata.Name} v{Info.Metadata.Version}");
            Logger.LogInfo($"Runtime: Unity={Application.unityVersion}, Game={Application.version}, Assembly-CSharp={typeof(MoodleManager).Assembly.GetName().Version}");

            // 1. 绑定热键（Debug 段；F8 设置窗口、F9 诊断 dump）
            _settingsKey = Config.Bind("Debug", "SettingsKey", KeyCode.F8,
                "Key to open/close the settings window.");
            _debugDumpKey = Config.Bind("Debug", "DebugDumpKey", KeyCode.F9,
                "Key to dump current Moodle diagnostics to the log.");

            // 2. 读取 BepInEx 配置（默认模板 + 兼容解析）
            var parseResult = LoadConfig();
            foreach (var warning in parseResult.Warnings)
            {
                Logger.LogWarning($"Config: {warning}");
            }

            // 3. 构建适配器：排序核心 + 提醒引擎 + 分发器 + 透明提醒展示
            _presentation = new ReminderPresentation();
            _overlay = new TransparentReminderOverlay(_presentation);
            var dispatcher = new ReminderDispatcher(Logger);
            var reminders = new ReminderEngine(parseResult.Config.Reminders);
            Adapter = new UnityUiAdapter(
                parseResult.Config.CreateSortPlan(),
                reminders,
                dispatcher,
                msg => Logger.LogInfo(msg),
                OnReminderMessage);

            // 4. rules 文件：首次无文件时从 ConfigFile 解析模型并写入；否则读取并应用。
            _rulesPath = Path.Combine(BepInEx.Paths.ConfigPath, RulesFileName);
            Logger.LogInfo($"Rules file: {_rulesPath}");
            _uiModel = LoadRulesModel(parseResult.Config);
            ApplyModel(_uiModel);

            // 5. F8 设置窗口（不依赖 ConfigurationManager）；初始状态目录来自当前捕获。
            SettingsWindow = new FallbackSettingsWindow(_uiModel, this, RefreshStateCatalog());

            // 6. Harmony 补丁：目标缺失时降级，不抛异常
            _harmony = new Harmony("com.healthautoarrange.plugin");
            try
            {
                var refreshMethod = AccessTools.Method(typeof(MoodleManager), "UpdateMoodles")
                    ?? AccessTools.Method(typeof(MoodleManager), "AddAllMoodles");
                if (refreshMethod == null)
                {
                    Logger.LogWarning("Neither MoodleManager.UpdateMoodles nor AddAllMoodles was found; sorting refresh hook disabled.");
                }
                else
                {
                    _harmony.Patch(
                        refreshMethod,
                        postfix: new HarmonyMethod(typeof(GamePatches), nameof(GamePatches.MoodleRefreshPostfix)));
                    Logger.LogInfo($"Patched Moodle refresh boundary: {refreshMethod.Name}.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to patch Moodle refresh boundary: {ex.Message}");
            }

            // AddMoodle 前缀只捕获元数据；刷新边界上面只选择一个方法，避免重复触发。
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
                        prefix: new HarmonyMethod(typeof(GamePatches), nameof(GamePatches.AddMoodlePrefix)));
                    Logger.LogInfo("Patched MoodleManager.AddMoodle capture.");
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
                    _harmony.Patch(
                        pointerOverUi,
                        prefix: new HarmonyMethod(typeof(GamePatches), nameof(GamePatches.IsPointerOverUIElementPrefix)));
                    Logger.LogInfo("Patched UIUtil.IsPointerOverUIElement input blocking.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to patch UIUtil.IsPointerOverUIElement: {ex.Message}");
            }

            Logger.LogInfo("HealthAutoArrange.Plugin loaded.");
        }

        /// <summary>
        /// 每帧驱动重试中的刷新；F8 切换设置窗口；F9 触发诊断 dump。
        /// </summary>
        private void Update()
        {
            try
            {
                Adapter?.Update();
                if (Input.GetKeyDown(_settingsKey.Value))
                {
                    ToggleSettingsWindow();
                }
                if (Input.GetKeyDown(_debugDumpKey.Value))
                {
                    Logger.LogInfo("F9 diagnostics requested.");
                    DumpDiagnostics();
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: Update error: {ex.Message}");
            }
        }

        /// <summary>
        /// IMGUI 设置窗口绘制 + 透明提醒 overlay 绘制。
        /// 两者各自隔离异常并记录，不影响游戏；overlay 不依赖 F8 窗口是否打开。
        /// </summary>
        private void OnGUI()
        {
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
        /// Save：Normalize → 写临时文件后替换 rules 文件 → 应用模型。
        /// 写文件失败仍应用内存模型；所有异常记录，不影响游戏。
        /// </summary>
        public void Save(UiConfigModel model)
        {
            try
            {
                model.Normalize();
                try
                {
                    RulesFileStore.Write(_rulesPath, model);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Failed to write rules file: {ex.Message}");
                }
                ApplyModel(model);
                _uiModel = model;
                Logger.LogInfo("Settings saved and applied.");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: Save failed: {ex.Message}");
            }
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
                ApplyModel(model);
                _uiModel = model;
                Logger.LogInfo("Settings reloaded from rules file.");
                return model;
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
                ShowDiagnosticsFeedback();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: Diagnostics failed: {ex.Message}");
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
                Logger.LogInfo("HealthAutoArrange diagnostics written to LogOutput.log");
                var camera = PlayerCamera.main;
                if (camera != null)
                {
                    camera.DoAlert("HealthAutoArrange diagnostics written to LogOutput.log", false);
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
                    ? entry.Intensities[entry.Intensities.Count - 1] : 0;
                var runtimeId = entry != null && !string.IsNullOrWhiteSpace(entry.LastRuntimeId)
                    ? entry.LastRuntimeId : model.Name;

                var context = new ReminderRenderContext(runtimeId, displayName, string.Empty, intensity);
                _presentation?.Preview(context, DateTimeOffset.UtcNow,
                    ReminderVisualPresetBuilder.Build(model), model.Template);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: PreviewReminder failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 正式提醒回调（适配器在提醒引擎触发时调用）：
        /// 从当前 UI 模型查找视觉配置（预设/模板/冷却），入队透明提醒展示。
        /// 与 ReminderEngine 构成双层去重，避免 0.5s 刷新周期内重复显示。
        /// </summary>
        private void OnReminderMessage(ReminderMessage message, ReminderRenderContext context)
        {
            try
            {
                var model = _uiModel?.Reminders?.FirstOrDefault(r => r != null
                    && string.Equals(r.Name, message.RuleName, StringComparison.OrdinalIgnoreCase));
                var preset = model != null ? ReminderVisualPresetBuilder.Build(model) : null;
                var template = model != null && !string.IsNullOrWhiteSpace(model.Template)
                    ? model.Template : null;
                var cooldown = model != null ? model.CooldownSeconds : 0d;
                _presentation?.Enqueue(message, context, DateTimeOffset.UtcNow, cooldown, preset, template);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: OnReminderMessage failed: {ex.Message}");
            }
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
        /// 应用模型：更新 ArrangeConfig、重建 ReminderEngine（经 Adapter.Reconfigure），
        /// 将 model.Enabled 传入运行时（禁用时不排序、不触发提醒），
        /// 保持当前 manager 与捕获注册表。
        /// </summary>
        private void ApplyModel(UiConfigModel model)
        {
            try
            {
                var config = model.ToConfig();
                Adapter?.Reconfigure(config, model.Enabled);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"HealthAutoArrange: ApplyModel failed: {ex.Message}");
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
            Config.Bind("Groups", "Group.Priority 1.States", string.Empty,
                "Highest-priority observed Moodle patterns. Prefer assigning them from the in-game state catalog.");
            Config.Bind("Groups", "Group.Priority 2.States", string.Empty,
                "Lower-priority observed Moodle patterns. Prefer assigning them from the in-game state catalog.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}