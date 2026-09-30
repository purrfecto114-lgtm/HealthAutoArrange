using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HealthAutoArrange.Core;
using UnityEngine;

namespace HealthAutoArrange.Plugin
{
    /// <summary>F8 设置窗口的宿主回调。实现方负责隔离 Unity/配置访问异常。</summary>
    public interface IFallbackSettingsActions
    {
        void Save(UiConfigModel model);
        UiConfigModel Reload();
        void ForceResort();
        void DumpDiagnostics();
        void Close();
    }

    public sealed class SettingsSaveResult
    {
        public bool Applied { get; }
        public bool Persisted { get; }
        public string Detail { get; }

        public SettingsSaveResult(bool applied, bool persisted, string detail)
        {
            Applied = applied;
            Persisted = persisted;
            Detail = detail ?? string.Empty;
        }
    }

    /// <summary>Optional richer persistence feedback used by the built-in UI.</summary>
    public interface IFallbackSettingsPersistenceActions
    {
        SettingsSaveResult SaveWithResult(UiConfigModel model);
    }

    /// <summary>Optional launch-only GitHub update status.</summary>
    public interface IFallbackSettingsUpdateActions
    {
        UpdateUiSnapshot GetUpdateStatus();
        void OpenUpdatePage();
        bool AutoCheckUpdates { get; set; }
    }

    /// <summary>可选的状态目录刷新能力，不破坏既有宿主回调接口。</summary>
    public interface IFallbackSettingsStateActions
    {
        IReadOnlyList<StateCatalogEntry> RefreshStateCatalog();
    }

    /// <summary>可选的提醒预览能力，不破坏既有宿主回调接口。</summary>
    public interface IFallbackSettingsPreviewActions
    {
        void PreviewReminder(UiReminderModel model);
    }

    /// <summary>Optional language persistence callback for the in-game GUI.</summary>
    public interface IFallbackSettingsLanguageActions
    {
        void SetChineseUi(bool chinese);
    }

    /// <summary>
    /// 不依赖 ConfigurationManager 的 Unity IMGUI 配置窗口。
    /// 调用方应在 OnGUI 中隔离 Draw() 抛出的 GUI 异常。
    /// </summary>
    public sealed class FallbackSettingsWindow
    {
        // v1.3.0 multi-window layout: one compact main window (basic settings, window
        // launchers, save/reload) plus three focused sub-windows (rules with tabs,
        // reminders, version & tools). All panes share the same DPI scale pipeline
        // (Screen.height / 1080, 1x-2x) and per-frame rect clamping.
        private const int MainWindowId = 187431;
        private const int RulesWindowId = 187432;
        private const int RemindersWindowId = 187433;
        private const int UpdatesWindowId = 187434;
        private readonly IFallbackSettingsActions _actions;
        private UiConfigModel _model;
        private UiTextCatalog _text;
        private IReadOnlyList<StateCatalogEntry> _stateCatalog;
        private GroupSelectionEditor _selectionEditor;
        private string _stateSearch = string.Empty;
        private int _stateFilter;
        private int _targetGroupIndex;
        private bool _dirty;
        private PendingDestructiveAction _pendingDestructiveAction;
        private string _stateMessage = string.Empty;
        private WindowPane _currentPane;  // pane whose delegate is drawing right now
        private readonly List<WindowPane> _drawOrderCache = new List<WindowPane>(4);

        internal enum WindowKind { Main, Rules, Reminders, Updates }

        /// <summary>Per-window state bag: rect, scroll, open flag and rules tab index.</summary>
        private sealed class WindowPane
        {
            public readonly WindowKind Kind;
            public readonly int Id;
            public readonly float MinWidth, MinHeight, MaxWidthRatio, MaxHeightRatio;
            public Rect Rect;
            public Vector2 Scroll;
            public bool Open;
            public bool HasOpened;
            public int Tab;
            public WindowPane(WindowKind kind, int id, Rect initialRect,
                float minWidth, float minHeight, float maxWidthRatio, float maxHeightRatio)
            {
                Kind = kind;
                Id = id;
                Rect = initialRect;
                MinWidth = minWidth;
                MinHeight = minHeight;
                MaxWidthRatio = maxWidthRatio;
                MaxHeightRatio = maxHeightRatio;
            }
        }

        private readonly WindowPane _mainPane = new WindowPane(
            WindowKind.Main, MainWindowId, new Rect(80f, 80f, 640f, 560f), 460f, 380f, 0.70f, 0.80f);
        private readonly WindowPane _rulesPane = new WindowPane(
            WindowKind.Rules, RulesWindowId, new Rect(150f, 120f, 560f, 520f), 460f, 380f, 0.70f, 0.80f);
        private readonly WindowPane _remindersPane = new WindowPane(
            WindowKind.Reminders, RemindersWindowId, new Rect(210f, 160f, 520f, 560f), 440f, 360f, 0.60f, 0.80f);
        private readonly WindowPane _updatesPane = new WindowPane(
            WindowKind.Updates, UpdatesWindowId, new Rect(260f, 200f, 460f, 360f), 360f, 240f, 0.50f, 0.60f);
        private WindowKind _lastActiveWindow = WindowKind.Main;

        private enum PendingDestructiveAction
        {
            None = 0,
            Close = 1,
            Reload = 2
        }
        private readonly Dictionary<UiReminderModel, Dictionary<string, NumericTextBuffer>> _numericBuffers
            = new Dictionary<UiReminderModel, Dictionary<string, NumericTextBuffer>>();

        private sealed class NumericTextBuffer
        {
            public string Text;
            public bool Editing;
            public Action<string> Commit;
        }

        public FallbackSettingsWindow(UiConfigModel model, IFallbackSettingsActions actions)
            : this(model, actions, new List<StateCatalogEntry>(),
                Application.systemLanguage.ToString().StartsWith("Chinese", StringComparison.OrdinalIgnoreCase))
        {
        }

        public FallbackSettingsWindow(
            UiConfigModel model,
            IFallbackSettingsActions actions,
            IReadOnlyList<StateCatalogEntry> stateCatalog)
            : this(model, actions, stateCatalog,
                Application.systemLanguage.ToString().StartsWith("Chinese", StringComparison.OrdinalIgnoreCase))
        {
        }

        public FallbackSettingsWindow(
            UiConfigModel model,
            IFallbackSettingsActions actions,
            IReadOnlyList<StateCatalogEntry> stateCatalog,
            bool chinese)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _actions = actions ?? throw new ArgumentNullException(nameof(actions));
            _text = UiTextCatalog.ForLanguage(chinese);
            _stateCatalog = NormalizeCatalog(stateCatalog);
            _selectionEditor = _model.CreateSelectionEditor();
        }

        public bool IsOpen => _mainPane.Open;
        /// <summary>Any of the four windows is open (input interception / badge move).</summary>
        public bool AnyOpen => _mainPane.Open || _rulesPane.Open || _remindersPane.Open || _updatesPane.Open;
        public UiConfigModel Model => _model;

        /// <summary>
        /// Sync the Enabled flag in the editable UI model from the runtime state.
        /// Called when an external hotkey (Ctrl+E) toggles Enabled without going through
        /// the F8 window. Keeps the F8 checkbox in sync the next time the window is opened.
        /// </summary>
        public void SyncEnabledFromRuntime(bool enabled)
        {
            try
            {
                if (_model != null) _model.Enabled = enabled;
                // No _dirty change here: the runtime already applied the change.
            }
            catch { /* best-effort UI sync; ignore */ }
        }

        public void Open()
        {
            _mainPane.Open = true;
            _mainPane.Scroll = Vector2.zero;
            ConfigurePaneRect(_mainPane, !_mainPane.HasOpened, 0f);
            _mainPane.HasOpened = true;
            _lastActiveWindow = WindowKind.Main;
            RefreshCatalog();
        }

        /// <summary>
        /// 打开窗口时从当前 Moodle UI 节点刷新状态目录；
        /// AddMoodle 捕获只补充元数据，不单独制造可选状态。
        /// </summary>
        private void RefreshCatalog()
        {
            try
            {
                var stateActions = _actions as IFallbackSettingsStateActions;
                if (stateActions == null) return;
                var refreshed = stateActions.RefreshStateCatalog();
                _stateCatalog = NormalizeCatalog(refreshed);
            }
            catch (Exception ex)
            {
                _stateMessage = _text.CatalogRefreshFailed + ex.Message;
            }
        }

        private void SetLanguage(bool chinese)
        {
            _text = UiTextCatalog.ForLanguage(chinese);
            var languageActions = _actions as IFallbackSettingsLanguageActions;
            languageActions?.SetChineseUi(chinese);
        }

        public void Close()
        {
            if (!AnyOpen) return;
            if (_dirty)
            {
                _pendingDestructiveAction = PendingDestructiveAction.Close;
                return;
            }
            CloseImmediately();
        }

        private void CloseImmediately()
        {
            _pendingDestructiveAction = PendingDestructiveAction.None;
            _mainPane.Open = false;
            _rulesPane.Open = false;
            _remindersPane.Open = false;
            _updatesPane.Open = false;
            _lastActiveWindow = WindowKind.Main;
            _actions.Close();
        }

        /// <summary>在 OnGUI 中调用；返回后由调用方决定是否继续绘制其它 UI。</summary>
        public void Draw()
        {
            if (!AnyOpen) return;
            if (Event.current != null && Event.current.type == EventType.KeyDown
                && Event.current.keyCode == KeyCode.Escape)
            {
                // Esc routes to the ACTIVE window: sub-windows close instantly (edits
                // live in the shared model, so nothing is lost); the main window keeps
                // the historical dirty-confirm semantics (F8 toggle unchanged).
                if (_lastActiveWindow == WindowKind.Main) Close();
                else ClosePane(_lastActiveWindow);
                Event.current.Use();
                return;
            }

            var previousMatrix = GUI.matrix;
            var scale = CalculateScale(Screen.height);
            try
            {
                GUI.matrix = previousMatrix * Matrix4x4.Scale(new Vector3(scale, scale, 1f));
                // Fixed z-order with the ACTIVE window drawn LAST (visually on top).
                _drawOrderCache.Clear();
                if (_mainPane.Open) _drawOrderCache.Add(_mainPane);
                if (_rulesPane.Open) _drawOrderCache.Add(_rulesPane);
                if (_remindersPane.Open) _drawOrderCache.Add(_remindersPane);
                if (_updatesPane.Open) _drawOrderCache.Add(_updatesPane);
                if (_lastActiveWindow != WindowKind.Main)
                {
                    var active = PaneFor(_lastActiveWindow);
                    if (_drawOrderCache.Remove(active)) _drawOrderCache.Add(active);
                }
                for (int i = 0; i < _drawOrderCache.Count; i++)
                {
                    DrawPaneWindow(_drawOrderCache[i], scale);
                }
                DrawTooltipOverlay(_drawOrderCache);
            }
            finally
            {
                GUI.matrix = previousMatrix;
            }
        }

        private void DrawPaneWindow(WindowPane pane, float scale)
        {
            ConfigurePaneRect(pane, false, scale);
            var previousPane = _currentPane;
            _currentPane = pane;
            try
            {
                pane.Rect = GUI.Window(pane.Id, pane.Rect, id => DrawPane(pane), TitleFor(pane.Kind));
            }
            finally
            {
                _currentPane = previousPane;
            }
        }

        private string TitleFor(WindowKind kind)
        {
            switch (kind)
            {
                case WindowKind.Rules: return _text.RulesWindowTitle;
                case WindowKind.Reminders: return _text.RemindersWindowTitle;
                case WindowKind.Updates: return _text.UpdatesWindowTitle;
                default: return _text.WindowTitle;
            }
        }

        private void DrawPane(WindowPane pane)
        {
            if (Event.current != null && Event.current.type == EventType.MouseDown)
            {
                _lastActiveWindow = pane.Kind;
                GUI.BringWindowToFront(pane.Id);
            }
            switch (pane.Kind)
            {
                case WindowKind.Rules: DrawRulesWindow(pane); break;
                case WindowKind.Reminders: DrawRemindersWindow(pane); break;
                case WindowKind.Updates: DrawUpdatesWindow(pane); break;
                default: DrawMainWindow(pane); break;
            }
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        }

        private void DrawMainWindow(WindowPane pane)
        {
            GUILayout.BeginVertical();
            DrawHeaderRow();

            pane.Scroll = GUILayout.BeginScrollView(pane.Scroll, GUILayout.ExpandHeight(true));

            DrawSectionHeader(_text.Basic, _text.EnabledHelp);
            DrawEnabledToggle();

            DrawSectionHeader(_text.UnknownStatePolicy, _text.UnknownPolicyHelp);
            var policy = DrawPolicy(_model.UnknownStatePolicy, _text);
            if (policy != _model.UnknownStatePolicy) { _model.UnknownStatePolicy = policy; _dirty = true; }
            if (_model.UnknownStatePolicy == UnknownStatePolicy.End)
                GUILayout.Label(_text.UnknownMovedNote);

            GUILayout.Space(6f);
            // v1.2.3: 同组内排序（效果强度降/升序 或 规则顺序）。
            DrawSectionHeader(_text.InGroupSort, _text.InGroupSortHelp);
            var inGroup = DrawInGroupSort(_model.InGroupSortMode, _text);
            if (inGroup != _model.InGroupSortMode) { _model.InGroupSortMode = inGroup; _dirty = true; }

            // v1.3.0: 档位变化弹入抑制。
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            var prevSuppress = _model.SuppressTierPopIn;
            var suppress = GUILayout.Toggle(prevSuppress, _text.SuppressTierPopIn, GUILayout.Height(26f));
            DrawInfoButton(_text.SuppressTierPopInHelp);
            GUILayout.EndHorizontal();
            if (suppress != prevSuppress) { _model.SuppressTierPopIn = suppress; _dirty = true; }

            GUILayout.Space(10f);
            DrawWindowLaunchers();

            GUILayout.EndScrollView();
            GUILayout.Space(6f);
            if (!string.IsNullOrEmpty(_stateMessage)) GUILayout.Label(_stateMessage);
            if (_dirty) GUILayout.Label("• " + _text.Unsaved);
            DrawPendingDestructivePrompt();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_text.Save, GUILayout.Width(IsNarrowLayout() ? 108f : 132f), GUILayout.Height(32f))) SaveChanges();
            if (GUILayout.Button(_text.Reload, GUILayout.Width(IsNarrowLayout() ? 118f : 148f), GUILayout.Height(32f)))
            {
                if (_dirty) _pendingDestructiveAction = PendingDestructiveAction.Reload;
                else ReloadFromDisk();
            }
            if (GUILayout.Button(_text.Close, GUILayout.Width(88f), GUILayout.Height(32f))) Close();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawHeaderRow()
        {
            GUILayout.BeginHorizontal();
            var updateActions = _actions as IFallbackSettingsUpdateActions;
            var headerUpdate = updateActions?.GetUpdateStatus();
            GUILayout.Label(headerUpdate == null ? "Health Auto Arrange" : "Health Auto Arrange  v" + headerUpdate.CurrentVersion, GUILayout.ExpandWidth(true), GUILayout.Height(26f));
            if (headerUpdate != null && headerUpdate.HasUpdate && !string.IsNullOrWhiteSpace(headerUpdate.LatestVersion))
            {
                if (GUILayout.Button(_text.UpdateBadge(headerUpdate.LatestVersion), GUILayout.Width(IsNarrowLayout() ? 108f : 126f), GUILayout.Height(26f)))
                    OpenPane(WindowKind.Updates);
            }
            if (GUILayout.Button(new GUIContent(_text.LanguageButton, _text.LanguageHelp), GUILayout.Width(76f), GUILayout.Height(26f)))
            {
                SetLanguage(!_text.IsChinese);
            }
            GUILayout.EndHorizontal();
        }

        private void DrawEnabledToggle()
        {
            var prevEnabled = _model.Enabled;
            var enabled = GUILayout.Toggle(prevEnabled, _text.Enabled, GUILayout.Height(26f));
            if (enabled != prevEnabled)
            {
                _model.Enabled = enabled;
                // Apply the master toggle immediately so the user sees the effect without
                // having to click Save. This is the fix for "mod switch failure" feedback:
                // users expected the toggle to disable sorting right away.
                var persistence = _actions as IFallbackSettingsPersistenceActions;
                if (persistence != null)
                {
                    try
                    {
                        var result = persistence.SaveWithResult(_model);
                        if (result.Applied)
                        {
                            _selectionEditor = _model.CreateSelectionEditor();
                            if (result.Persisted)
                            {
                                _dirty = false;
                                _stateMessage = _text.SaveSucceeded;
                                _pendingDestructiveAction = PendingDestructiveAction.None;
                            }
                            else
                            {
                                _dirty = true;
                                _stateMessage = _text.SaveMemoryOnly;
                            }
                        }
                        else
                        {
                            _dirty = true;
                            _stateMessage = _text.SaveFailed
                                + (string.IsNullOrWhiteSpace(result.Detail) ? string.Empty : " " + result.Detail);
                        }
                    }
                    catch (Exception ex)
                    {
                        _dirty = true;
                        _stateMessage = _text.SaveFailed + ex.Message;
                    }
                }
                else
                {
                    // No persistence callback; fall back to legacy Save() which still applies at runtime.
                    try { _actions.Save(_model); _dirty = false; _stateMessage = _text.SaveSucceeded; }
                    catch (Exception ex) { _dirty = true; _stateMessage = _text.SaveFailed + ex.Message; }
                }
            }
        }

        private void DrawWindowLaunchers()
        {
            DrawSectionHeader(_text.WindowsHeader, _text.WindowsHelp);
            GUILayout.BeginHorizontal();
            LauncherButton(WindowKind.Rules, _text.RulesWindowTitle);
            LauncherButton(WindowKind.Reminders, _text.RemindersWindowTitle);
            LauncherButton(WindowKind.Updates, _text.UpdatesWindowTitle);
            GUILayout.EndHorizontal();
        }

        private void LauncherButton(WindowKind kind, string label)
        {
            var pane = PaneFor(kind);
            var marker = pane.Open ? " \u2713" : string.Empty;
            if (GUILayout.Button(label + marker, GUILayout.Height(30f)))
            {
                if (pane.Open) ClosePane(kind);
                else OpenPane(kind);
            }
        }

        internal void OpenPane(WindowKind kind)
        {
            var pane = PaneFor(kind);
            if (pane.Open) return;
            pane.Open = true;
            pane.HasOpened = true;
            pane.Scroll = Vector2.zero;
            // Cascade from the main window so the new pane never covers its header.
            var anchor = _mainPane.Rect;
            pane.Rect = new Rect(anchor.x + 28f, anchor.y + 28f, pane.Rect.width, pane.Rect.height);
            ConfigurePaneRect(pane, false, 0f);
            _lastActiveWindow = kind;
            GUI.BringWindowToFront(pane.Id);
        }

        private void ClosePane(WindowKind kind)
        {
            var pane = PaneFor(kind);
            pane.Open = false;
            if (_lastActiveWindow == kind) _lastActiveWindow = WindowKind.Main;
        }

        private WindowPane PaneFor(WindowKind kind)
        {
            switch (kind)
            {
                case WindowKind.Rules: return _rulesPane;
                case WindowKind.Reminders: return _remindersPane;
                case WindowKind.Updates: return _updatesPane;
                default: return _mainPane;
            }
        }

        private void DrawRulesWindow(WindowPane pane)
        {
            GUILayout.BeginVertical();
            // v1.3.0 multi-page: the rules window packs three focused pages.
            var tabs = new[] { _text.Groups, _text.StateSelection, _text.TechnicalEditing };
            if (pane.Tab < 0 || pane.Tab >= tabs.Length) pane.Tab = 0;
            pane.Tab = GUILayout.Toolbar(pane.Tab, tabs);
            GUILayout.Space(6f);
            pane.Scroll = GUILayout.BeginScrollView(pane.Scroll, GUILayout.ExpandHeight(true));
            switch (pane.Tab)
            {
                case 0:
                    DrawSectionHeader(_text.Groups, _text.GroupHelp);
                    DrawGroups();
                    break;
                case 1:
                    DrawStateSelection();
                    break;
                default:
                    DrawSectionHeader(_text.TechnicalEditing, _text.AdvancedHelp);
                    DrawAdvancedGroupTextEditor();
                    break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawRemindersWindow(WindowPane pane)
        {
            GUILayout.BeginVertical();
            DrawSectionHeader(_text.ReminderRules, _text.ReminderHelp);
            pane.Scroll = GUILayout.BeginScrollView(pane.Scroll, GUILayout.ExpandHeight(true));
            DrawReminders();
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawUpdatesWindow(WindowPane pane)
        {
            GUILayout.BeginVertical();
            pane.Scroll = GUILayout.BeginScrollView(pane.Scroll, GUILayout.ExpandHeight(true));
            DrawUpdates();
            GUILayout.Space(10f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_text.ForceResort, GUILayout.Height(30f))) _actions.ForceResort();
            if (GUILayout.Button(_text.Diagnostics, GUILayout.Height(30f))) _actions.DumpDiagnostics();
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void SaveChanges()
        {
            CommitPendingNumericFields();
            ApplySelectionToModel();
            SyncGroupOrderFromGroups();
            _model.Normalize();

            var persistence = _actions as IFallbackSettingsPersistenceActions;
            if (persistence != null)
            {
                var result = persistence.SaveWithResult(_model);
                if (result.Applied)
                    _selectionEditor = _model.CreateSelectionEditor();
                if (result.Persisted && result.Applied)
                {
                    _dirty = false;
                    _stateMessage = _text.SaveSucceeded;
                    _pendingDestructiveAction = PendingDestructiveAction.None;
                }
                else if (result.Applied)
                {
                    _dirty = true;
                    _stateMessage = _text.SaveMemoryOnly + (string.IsNullOrWhiteSpace(result.Detail) ? string.Empty : " " + result.Detail);
                }
                else if (result.Persisted)
                {
                    _dirty = true;
                    _stateMessage = _text.SaveDiskOnly + (string.IsNullOrWhiteSpace(result.Detail) ? string.Empty : " " + result.Detail);
                }
                else
                {
                    _dirty = true;
                    _stateMessage = _text.SaveFailed + (string.IsNullOrWhiteSpace(result.Detail) ? string.Empty : " " + result.Detail);
                }
                return;
            }

            _actions.Save(_model);
            _selectionEditor = _model.CreateSelectionEditor();
            _dirty = false;
            _stateMessage = _text.SaveSucceeded;
            _pendingDestructiveAction = PendingDestructiveAction.None;
        }

        private bool ReloadFromDisk()
        {
            var loaded = _actions.Reload();
            if (loaded == null)
            {
                _stateMessage = _text.ReloadFailed;
                return false;
            }
            _model = loaded;
            _selectionEditor = _model.CreateSelectionEditor();
            _numericBuffers.Clear();
            _dirty = false;
            _stateMessage = _text.ReloadSucceeded;
            _pendingDestructiveAction = PendingDestructiveAction.None;
            return true;
        }

        private void DrawPendingDestructivePrompt()
        {
            if (_pendingDestructiveAction == PendingDestructiveAction.None) return;
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(_pendingDestructiveAction == PendingDestructiveAction.Close
                ? _text.UnsavedClosePrompt
                : _text.UnsavedReloadPrompt);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_text.Save, GUILayout.Height(30f)))
            {
                var action = _pendingDestructiveAction;
                SaveChanges();
                if (!_dirty)
                {
                    if (action == PendingDestructiveAction.Close) CloseImmediately();
                    else if (action == PendingDestructiveAction.Reload) ReloadFromDisk();
                }
            }
            if (GUILayout.Button(_text.Discard, GUILayout.Height(30f)))
            {
                var action = _pendingDestructiveAction;
                if (ReloadFromDisk() && action == PendingDestructiveAction.Close) CloseImmediately();
            }
            if (GUILayout.Button(_text.Cancel, GUILayout.Height(30f)))
                _pendingDestructiveAction = PendingDestructiveAction.None;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawUpdates()
        {
            var updateActions = _actions as IFallbackSettingsUpdateActions;
            if (updateActions == null)
            {
                GUILayout.Label(_text.UpdaterUnavailable);
                return;
            }

            DrawSectionHeader(_text.Updates, _text.UpdateSecurityHelp);
            var autoCheck = GUILayout.Toggle(updateActions.AutoCheckUpdates, _text.AutoCheckUpdates, GUILayout.Height(24f));
            if (autoCheck != updateActions.AutoCheckUpdates) updateActions.AutoCheckUpdates = autoCheck;

            var snapshot = updateActions.GetUpdateStatus();
            GUILayout.Label(_text.CurrentVersion + ": " + snapshot.CurrentVersion);
            if (!string.IsNullOrWhiteSpace(snapshot.LatestVersion))
                GUILayout.Label(_text.LatestVersion + ": " + snapshot.LatestVersion);
            GUILayout.Label(_text.UpdateState(snapshot.State));
            if (!string.IsNullOrWhiteSpace(snapshot.Detail))
            {
                var detailStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
                GUILayout.Label(snapshot.Detail, detailStyle, GUILayout.ExpandWidth(true));
            }
            GUI.enabled = snapshot.HasUpdate;
            if (GUILayout.Button(_text.ReleaseNotes, GUILayout.Height(30f), GUILayout.ExpandWidth(true))) updateActions.OpenUpdatePage();
            GUI.enabled = true;
            var noteStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            GUILayout.Label(_text.UpdateInstallNote, noteStyle, GUILayout.ExpandWidth(true));
        }

        private void DrawGroups()
        {
            if (_model.Groups.Count == 0) GUILayout.Label(_text.NoGroup);
            var delete = -1;
            var move = 0;
            for (int i = 0; i < _model.Groups.Count; i++)
            {
                var group = _model.Groups[i];
                var count = SplitList(group.StatesText).Count;
                GUILayout.BeginHorizontal();
                GUILayout.Label((i + 1) + ". " + group.Name + "  (" + count + ")", GUILayout.ExpandWidth(true));
                GUI.enabled = i > 0;
                if (GUILayout.Button("↑", GUILayout.Width(34f), GUILayout.Height(28f))) { move = -1; delete = i; }
                GUI.enabled = i < _model.Groups.Count - 1;
                if (GUILayout.Button("↓", GUILayout.Width(34f), GUILayout.Height(28f))) { move = 1; delete = i; }
                GUI.enabled = true;
                if (GUILayout.Button(_text.Delete, GUILayout.Width(70f), GUILayout.Height(28f))) { move = 99; delete = i; }
                GUILayout.EndHorizontal();
            }

            if (delete >= 0)
            {
                if (move == 99) _model.Groups.RemoveAt(delete);
                else
                {
                    var target = delete + move;
                    var item = _model.Groups[delete];
                    _model.Groups.RemoveAt(delete);
                    _model.Groups.Insert(target, item);
                }
                SyncGroupOrderFromGroups();
                _selectionEditor = _model.CreateSelectionEditor();
                _dirty = true;
            }

            if (GUILayout.Button(_text.AddGroup, GUILayout.Width(118f), GUILayout.Height(28f)))
            {
                _model.Groups.Add(new UiGroupModel(NextGroupName(), string.Empty));
                SyncGroupOrderFromGroups();
                _selectionEditor = _model.CreateSelectionEditor();
                _dirty = true;
            }
        }

        private string NextGroupName()
        {
            for (int i = 1; i < 100; i++)
            {
                var candidate = _text.PriorityGroupPrefix + i;
                if (!_model.Groups.Any(g => string.Equals(g.Name, candidate, StringComparison.OrdinalIgnoreCase))) return candidate;
            }
            return _text.NewGroup;
        }

        private void DrawAdvancedGroupTextEditor()
        {
            var changed = false;
            var delete = new List<int>();
            for (int i = 0; i < _model.Groups.Count; i++)
            {
                var group = _model.Groups[i];
                var beforeName = group.Name ?? string.Empty;
                var beforeStates = group.StatesText ?? string.Empty;
                if (IsNarrowLayout()) DrawNarrowGroup(group, i, delete, _text);
                else DrawWideGroup(group, i, delete, _text);
                changed |= !string.Equals(beforeName, group.Name, StringComparison.Ordinal)
                    || !string.Equals(beforeStates, group.StatesText, StringComparison.Ordinal);
            }
            if (delete.Count > 0) { RemoveDescending(_model.Groups, delete); changed = true; }
            if (changed)
            {
                SyncGroupOrderFromGroups();
                _selectionEditor = _model.CreateSelectionEditor();
                _dirty = true;
            }
        }

        private void DrawReminders()
        {
            var remindersToDelete = new List<int>();
            for (int i = 0; i < _model.Reminders.Count; i++)
            {
                var rule = _model.Reminders[i];
                var before = ReminderFingerprint(rule);
                DrawReminder(rule, i, remindersToDelete, IsNarrowLayout(), _text);
                if (!string.Equals(before, ReminderFingerprint(rule), StringComparison.Ordinal)) _dirty = true;
            }
            if (GUILayout.Button(_text.AddReminder, GUILayout.Width(124f), GUILayout.Height(28f)))
            {
                var first = _stateCatalog.FirstOrDefault(x => x != null);
                _model.Reminders.Add(new UiReminderModel(
                    first != null ? first.Pattern : string.Empty,
                    false,
                    ReminderMode.Log,
                    ReminderRepeatMode.Once,
                    ReminderRule.DefaultPeriodSeconds,
                    ReminderRule.DefaultSendsPerPeriod));
                _dirty = true;
            }
            if (remindersToDelete.Count > 0) { RemoveDescending(_model.Reminders, remindersToDelete); _dirty = true; }
        }

        private static string ReminderFingerprint(UiReminderModel rule)
        {
            if (rule == null) return string.Empty;
            var p = rule.Placement;
            return string.Join("|", new[]
            {
                rule.Name ?? string.Empty,
                rule.Enabled.ToString(),
                ((int)rule.Mode).ToString(CultureInfo.InvariantCulture),
                ((int)rule.RepeatMode).ToString(CultureInfo.InvariantCulture),
                rule.PeriodSeconds.ToString("R", CultureInfo.InvariantCulture),
                rule.SendsPerPeriod.ToString(CultureInfo.InvariantCulture),
                rule.Template ?? string.Empty,
                ((int)rule.PresetKind).ToString(CultureInfo.InvariantCulture),
                rule.Opacity.ToString("R", CultureInfo.InvariantCulture),
                rule.DurationSeconds.ToString("R", CultureInfo.InvariantCulture),
                p == null ? string.Empty : ((int)p.Preset).ToString(CultureInfo.InvariantCulture),
                p == null ? string.Empty : p.NormalizedX.ToString("R", CultureInfo.InvariantCulture),
                p == null ? string.Empty : p.NormalizedY.ToString("R", CultureInfo.InvariantCulture),
                p == null ? string.Empty : p.PixelOffsetX.ToString("R", CultureInfo.InvariantCulture),
                p == null ? string.Empty : p.PixelOffsetY.ToString("R", CultureInfo.InvariantCulture)
            });
        }

        private void DrawStateSelection()
        {
            GUILayout.Space(10f);
            DrawSectionHeader(_text.StateSelection, _text.CatalogHelp);
            GUILayout.BeginHorizontal();
            GUILayout.Label(_text.Search, GUILayout.Width(52f));
            _stateSearch = GUILayout.TextField(_stateSearch ?? string.Empty, GUILayout.ExpandWidth(true), GUILayout.Height(28f));
            if (GUILayout.Button(_text.RefreshStates, GUILayout.Width(82f), GUILayout.Height(28f))) RefreshCatalog();
            GUILayout.EndHorizontal();

            var filters = new[] { _text.All, _text.Unassigned, _text.Current };
            var filterWidth = IsNarrowLayout() ? 98f : 132f;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < filters.Length; i++)
            {
                var prefix = _stateFilter == i ? "● " : string.Empty;
                if (GUILayout.Button(prefix + filters[i], GUILayout.Width(filterWidth), GUILayout.Height(28f))) _stateFilter = i;
            }
            GUILayout.EndHorizontal();

            var groups = _selectionEditor.Groups.Where(g => !string.IsNullOrWhiteSpace(g.Name)).ToList();
            if (groups.Count > 0)
            {
                if (_targetGroupIndex >= groups.Count) _targetGroupIndex = 0;
                GUILayout.BeginHorizontal();
                DrawLabelWithInfo(_text.TargetGroup, _text.TargetGroupHelp, 90f);
                var target = groups[_targetGroupIndex].Name;
                if (GUILayout.Button(target + " ▼", GUILayout.ExpandWidth(true), GUILayout.Height(28f)))
                    _targetGroupIndex = (_targetGroupIndex + 1) % groups.Count;
                GUILayout.EndHorizontal();
            }
            else GUILayout.Label(_text.NoGroup);

            var now = DateTimeOffset.UtcNow;
            var search = (_stateSearch ?? string.Empty).Trim();
            foreach (var entry in _stateCatalog)
            {
                if (entry == null) continue;
                if (search.Length > 0 && entry.BaseId.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0
                    && entry.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var assigned = FindAssignedGroup(entry.BaseId);
                if (_stateFilter == 1 && assigned != null) continue;
                if (_stateFilter == 2 && (now - entry.LastSeenAt).TotalSeconds > 30d) continue;

                GUILayout.BeginHorizontal();
                var displayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.BaseId : entry.DisplayName;
                GUILayout.Label(displayName, GUILayout.ExpandWidth(true), GUILayout.Height(28f));
                DrawInfoButton(_text.StateTechnicalHelp(entry));
                if (assigned == null)
                {
                    if (groups.Count > 0 && GUILayout.Button(_text.Join, GUILayout.Width(76f), GUILayout.Height(28f)))
                        AddStateToTarget(entry.BaseId, groups);
                }
                else
                {
                    GUILayout.Label(assigned.Name, GUILayout.Width(IsNarrowLayout() ? 92f : 132f));
                    if (groups.Count > 0 && !string.Equals(assigned.Name, groups[_targetGroupIndex].Name, StringComparison.OrdinalIgnoreCase)
                        && GUILayout.Button(_text.Move, GUILayout.Width(68f), GUILayout.Height(28f)))
                        MoveState(entry.BaseId, assigned.Name, groups[_targetGroupIndex].Name);
                    if (GUILayout.Button(_text.Remove, GUILayout.Width(68f), GUILayout.Height(28f)))
                    {
                        _selectionEditor.RemoveState(assigned.Name, entry.BaseId);
                        ApplySelectionToModel();
                    }
                }
                GUILayout.EndHorizontal();
            }
            if (_stateCatalog.Count == 0) GUILayout.Label(_text.NoStates);
        }

        private GroupSelection FindAssignedGroup(string baseId)
        {
            var normalized = MoodleIdentity.NormalizeRuntimeId(baseId);
            return _selectionEditor.Groups.FirstOrDefault(g => g.States.Any(s =>
                string.Equals(MoodleIdentity.NormalizeRuntimeId(s), normalized, StringComparison.OrdinalIgnoreCase)));
        }

        private static IReadOnlyList<StateCatalogEntry> NormalizeCatalog(IEnumerable<StateCatalogEntry> entries)
        {
            var result = new List<StateCatalogEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (entries == null) return result;
            foreach (var entry in entries)
            {
                if (entry != null && seen.Add(entry.BaseId)) result.Add(entry);
            }
            return result;
        }

        private void AddStateToTarget(string baseId, List<GroupSelection> groups)
        {
            var result = _selectionEditor.AddState(groups[_targetGroupIndex].Name, baseId);
            _stateMessage = result.Added ? string.Empty : _text.Conflict + (result.ConflictGroup ?? string.Empty);
            if (result.Added) ApplySelectionToModel();
        }

        private void MoveState(string baseId, string fromGroup, string toGroup)
        {
            _selectionEditor.RemoveState(fromGroup, baseId);
            var result = _selectionEditor.AddState(toGroup, baseId);
            if (!result.Added)
            {
                _stateMessage = _text.Conflict + (result.ConflictGroup ?? string.Empty);
                _selectionEditor.AddState(fromGroup, baseId);
            }
            else
            {
                _stateMessage = string.Empty;
                ApplySelectionToModel();
            }
        }

        private void ApplySelectionToModel()
        {
            _model.ApplySelectionEditor(_selectionEditor);
            _dirty = true;
        }

        private void SyncGroupOrderFromGroups()
        {
            _model.GroupOrder = _model.Groups
                .Where(g => g != null && !string.IsNullOrWhiteSpace(g.Name))
                .Select(g => g.Name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void DrawWideGroup(UiGroupModel group, int index, List<int> groupsToDelete, UiTextCatalog text)
        {
            GUILayout.BeginHorizontal();
            group.Name = GUILayout.TextField(group.Name ?? string.Empty, GUILayout.Width(150f), GUILayout.Height(28f));
            group.StatesText = GUILayout.TextField(group.StatesText ?? string.Empty, GUILayout.ExpandWidth(true), GUILayout.Height(28f));
            if (GUILayout.Button(text.Delete, GUILayout.Width(70f), GUILayout.Height(28f)))
                groupsToDelete.Add(index);
            GUILayout.EndHorizontal();
        }

        private static void DrawNarrowGroup(UiGroupModel group, int index, List<int> groupsToDelete, UiTextCatalog text)
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label(text.Name, GUILayout.Width(52f));
            group.Name = GUILayout.TextField(group.Name ?? string.Empty, GUILayout.ExpandWidth(true), GUILayout.Height(28f));
            if (GUILayout.Button(text.Delete, GUILayout.Width(70f), GUILayout.Height(28f)))
                groupsToDelete.Add(index);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label(text.States, GUILayout.Width(52f));
            group.StatesText = GUILayout.TextField(group.StatesText ?? string.Empty, GUILayout.ExpandWidth(true), GUILayout.Height(28f));
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
            GUILayout.EndVertical();
        }

        private void DrawReminder(UiReminderModel rule, int index, List<int> remindersToDelete, bool narrow, UiTextCatalog text)
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            rule.Enabled = GUILayout.Toggle(rule.Enabled, string.Empty, GUILayout.Width(24f), GUILayout.Height(28f));
            GUILayout.Label(text.Name, GUILayout.Width(52f));
            DrawReminderState(rule, narrow);
            if (GUILayout.Button(text.Delete, GUILayout.Width(70f), GUILayout.Height(28f)))
                remindersToDelete.Add(index);
            GUILayout.EndHorizontal();
            GUILayout.Label(text.Mode);
            DrawReminderMode(rule, narrow, text);
            DrawReminderFrequency(rule, narrow, text);
            GUILayout.Label(text.VisualPreset);
            DrawVisualPreset(rule, narrow, text);
            GUILayout.Label(text.Template);
            rule.Template = GUILayout.TextField(rule.Template ?? ReminderTemplateFormatter.DefaultTemplate,
                GUILayout.ExpandWidth(true), GUILayout.Height(28f));
            if (narrow)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(text.Opacity, GUILayout.Width(110f));
                DrawFloatField(rule, "opacity", rule.Opacity, 0f, 1f, 110f, value => rule.Opacity = value);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label(text.Duration, GUILayout.Width(110f));
                DrawFloatField(rule, "duration", rule.DurationSeconds, 0.1f, 600f, 110f, value => rule.DurationSeconds = value);
                GUILayout.EndHorizontal();
                if (GUILayout.Button(text.Preview, GUILayout.ExpandWidth(true), GUILayout.Height(28f)))
                {
                    var previewActions = _actions as IFallbackSettingsPreviewActions;
                    if (previewActions != null) previewActions.PreviewReminder(rule);
                }
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(text.Opacity, GUILayout.Width(90f));
                DrawFloatField(rule, "opacity", rule.Opacity, 0f, 1f, 92f, value => rule.Opacity = value);
                GUILayout.Label(text.Duration, GUILayout.Width(90f));
                DrawFloatField(rule, "duration", rule.DurationSeconds, 0.1f, 600f, 100f, value => rule.DurationSeconds = value);
                if (GUILayout.Button(text.Preview, GUILayout.Width(88f), GUILayout.Height(28f)))
                {
                    var previewActions = _actions as IFallbackSettingsPreviewActions;
                    if (previewActions != null) previewActions.PreviewReminder(rule);
                }
                GUILayout.EndHorizontal();
            }
            DrawPlacement(rule, text, narrow);
            GUILayout.Space(6f);
            GUILayout.EndVertical();
        }

        private void DrawReminderState(UiReminderModel rule, bool narrow)
        {
            if (_stateCatalog.Count == 0)
            {
                GUILayout.Label(_text.NoStates, GUILayout.ExpandWidth(true));
                return;
            }

            var reminderBaseId = MoodleIdentity.PatternBaseId(rule.Name);
            var entry = _stateCatalog.FirstOrDefault(x => x != null
                && string.Equals(x.BaseId, reminderBaseId, StringComparison.OrdinalIgnoreCase));
            var index = entry == null ? -1 : _stateCatalog.ToList().FindIndex(x => x != null
                && string.Equals(x.BaseId, entry.BaseId, StringComparison.OrdinalIgnoreCase));
            var label = entry == null
                ? _text.SelectState
                : (string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.BaseId : entry.DisplayName);

            if (GUILayout.Button(label, narrow ? GUILayout.ExpandWidth(true) : GUILayout.Width(260f), GUILayout.Height(28f)))
            {
                var nextIndex = index < 0 ? 0 : (index + 1) % _stateCatalog.Count;
                var next = _stateCatalog[nextIndex];
                if (next != null) { rule.Name = next.Pattern; _dirty = true; }
            }
        }

        private static void DrawReminderMode(UiReminderModel rule, bool narrow, UiTextCatalog text)
        {
            if (rule.Mode == ReminderMode.HealthPanelHint)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(text.ReminderMode(rule.Mode), GUILayout.ExpandWidth(true));
                if (GUILayout.Button(text.UseLog, GUILayout.Width(96f), GUILayout.Height(28f)))
                    rule.Mode = ReminderMode.Log;
                GUILayout.EndHorizontal();
                return;
            }

            var modes = new[] { text.ReminderMode(ReminderMode.Log), text.ReminderMode(ReminderMode.BottomAlert) };
            var selected = rule.Mode == ReminderMode.BottomAlert ? 1 : 0;
            if (narrow)
            {
                var next = GUILayout.SelectionGrid(selected, modes, 1, GUILayout.ExpandWidth(true), GUILayout.Height(56f));
                if (next != selected) rule.Mode = next == 1 ? ReminderMode.BottomAlert : ReminderMode.Log;
            }
            else if (GUILayout.Button(modes[selected], GUILayout.Width(220f), GUILayout.Height(28f)))
            {
                rule.Mode = selected == 0 ? ReminderMode.BottomAlert : ReminderMode.Log;
            }
        }

        private static void DrawVisualPreset(UiReminderModel rule, bool narrow, UiTextCatalog text)
        {
            var kinds = new[]
            {
                ReminderVisualPresetKind.SubtleBottom,
                ReminderVisualPresetKind.SubtleTop,
                ReminderVisualPresetKind.CriticalCenter,
                ReminderVisualPresetKind.CompactBottomLeft
            };
            var selected = Array.IndexOf(kinds, rule.PresetKind);
            if (selected < 0) selected = 0;
            if (GUILayout.Button(text.ReminderPreset(kinds[selected]),
                narrow ? GUILayout.ExpandWidth(true) : GUILayout.Width(220f), GUILayout.Height(28f)))
            {
                rule.ApplyPreset(kinds[(selected + 1) % kinds.Length]);
            }
        }

        private void DrawPlacement(UiReminderModel rule, UiTextCatalog text, bool narrow)
        {
            var presets = Enum.GetValues(typeof(ReminderPlacementPreset));
            var current = rule.Placement == null ? ReminderPlacementPreset.Bottom : rule.Placement.Preset;
            var selected = (int)current;
            GUILayout.BeginHorizontal();
            GUILayout.Label(text.Placement, GUILayout.Width(90f));
            if (GUILayout.Button(text.PlacementPreset(current), narrow ? GUILayout.ExpandWidth(true) : GUILayout.Width(180f), GUILayout.Height(28f)))
            {
                selected = (selected + 1) % presets.Length;
                rule.Placement = PlacementFor((ReminderPlacementPreset)selected, rule.Placement);
            }
            GUILayout.EndHorizontal();
            current = rule.Placement == null ? ReminderPlacementPreset.Bottom : rule.Placement.Preset;
            if (current != ReminderPlacementPreset.Custom) return;

            var placement = rule.Placement ?? ReminderPlacements.Custom(0.5f, 0.5f, 0f, 0f);
            float x, y, px, py;
            if (narrow)
            {
                x = DrawLabeledFloat(rule, "normalized-x", text.NormalizedX, placement.NormalizedX, 0f, 1f, 116f);
                y = DrawLabeledFloat(rule, "normalized-y", text.NormalizedY, placement.NormalizedY, 0f, 1f, 116f);
                px = DrawLabeledFloat(rule, "pixel-offset-x", text.PixelOffsetX, placement.PixelOffsetX, -2000f, 2000f, 116f);
                py = DrawLabeledFloat(rule, "pixel-offset-y", text.PixelOffsetY, placement.PixelOffsetY, -2000f, 2000f, 116f);
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(text.NormalizedX, GUILayout.Width(100f));
                x = placement.NormalizedX;
                DrawFloatField(rule, "normalized-x", x, 0f, 1f, 90f,
                    value => SetCustomPlacementComponent(rule, "normalized-x", value));
                GUILayout.Label(text.NormalizedY, GUILayout.Width(100f));
                y = placement.NormalizedY;
                DrawFloatField(rule, "normalized-y", y, 0f, 1f, 90f,
                    value => SetCustomPlacementComponent(rule, "normalized-y", value));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label(text.PixelOffsetX, GUILayout.Width(100f));
                px = placement.PixelOffsetX;
                DrawFloatField(rule, "pixel-offset-x", px, -2000f, 2000f, 90f,
                    value => SetCustomPlacementComponent(rule, "pixel-offset-x", value));
                GUILayout.Label(text.PixelOffsetY, GUILayout.Width(100f));
                py = placement.PixelOffsetY;
                DrawFloatField(rule, "pixel-offset-y", py, -2000f, 2000f, 90f,
                    value => SetCustomPlacementComponent(rule, "pixel-offset-y", value));
                GUILayout.EndHorizontal();
            }
        }

        private static ReminderPlacement PlacementFor(ReminderPlacementPreset preset, ReminderPlacement previous)
        {
            switch (preset)
            {
                case ReminderPlacementPreset.Top: return ReminderPlacements.Top();
                case ReminderPlacementPreset.Center: return ReminderPlacements.Center();
                case ReminderPlacementPreset.BottomLeft: return ReminderPlacements.BottomLeft();
                case ReminderPlacementPreset.Custom:
                    return ReminderPlacements.Custom(previous == null ? 0.5f : previous.NormalizedX,
                        previous == null ? 0.5f : previous.NormalizedY,
                        previous == null ? 0f : previous.PixelOffsetX,
                        previous == null ? 0f : previous.PixelOffsetY);
                default: return ReminderPlacements.Bottom();
            }
        }

        private void DrawFloatField(UiReminderModel rule, string field, float value, float min, float max,
            float width, Action<float> commit = null)
        {
            DrawNumericField(rule, field, FormatFloat(value), width, text =>
            {
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    && NumericSafety.IsFinite(parsed))
                    commit?.Invoke(Mathf.Clamp(parsed, min, max));
            });
        }

        private float DrawLabeledFloat(UiReminderModel rule, string field, string label, float value,
            float min, float max, float labelWidth)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(labelWidth));
            DrawFloatField(rule, field, value, min, max, 110f,
                parsed => SetCustomPlacementComponent(rule, field, parsed));
            GUILayout.EndHorizontal();
            return GetCustomPlacementComponent(rule, field, value);
        }

        private void DrawNumericField(UiReminderModel rule, string field, string modelText, float width,
            Action<string> commit)
        {
            if (!_numericBuffers.TryGetValue(rule, out var fields))
            {
                fields = new Dictionary<string, NumericTextBuffer>();
                _numericBuffers[rule] = fields;
            }
            if (!fields.TryGetValue(field, out var buffer))
            {
                buffer = new NumericTextBuffer { Text = modelText };
                fields[field] = buffer;
            }
            buffer.Commit = commit;

            var controlName = "numeric-" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(rule)
                + "-" + field;
            var focused = GUI.GetNameOfFocusedControl();
            if (buffer.Editing && Event.current != null && Event.current.type != EventType.Layout
                && Event.current.type != EventType.Repaint && !string.Equals(focused, controlName, StringComparison.Ordinal))
            {
                commit(buffer.Text);
                buffer.Editing = false;
            }
            if (!buffer.Editing) buffer.Text = modelText;

            GUI.SetNextControlName(controlName);
            var edited = GUILayout.TextField(buffer.Text ?? string.Empty, GUILayout.Width(width), GUILayout.Height(28f));
            if (!string.Equals(edited, buffer.Text, StringComparison.Ordinal))
            {
                buffer.Text = edited;
                buffer.Editing = true;
            }
            if (Event.current != null && Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                && string.Equals(GUI.GetNameOfFocusedControl(), controlName, StringComparison.Ordinal))
            {
                commit(buffer.Text);
                buffer.Editing = false;
                Event.current.Use();
            }
        }

        private void CommitPendingNumericFields()
        {
            foreach (var fields in _numericBuffers.Values)
            {
                foreach (var buffer in fields.Values)
                {
                    if (!buffer.Editing) continue;
                    buffer.Commit?.Invoke(buffer.Text);
                    buffer.Editing = false;
                }
            }
        }

        private static string FormatFloat(float value)
        {
            return NumericSafety.IsFinite(value)
                ? value.ToString("R", CultureInfo.InvariantCulture)
                : "0";
        }

        private static void SetCustomPlacementValue(UiReminderModel rule, float x, float y, float px, float py)
        {
            var old = rule.Placement;
            if (old != null && old.Preset == ReminderPlacementPreset.Custom
                && old.NormalizedX == x && old.NormalizedY == y
                && old.PixelOffsetX == px && old.PixelOffsetY == py) return;
            rule.Placement = ReminderPlacements.Custom(x, y, px, py);
        }

        private static void SetCustomPlacementComponent(UiReminderModel rule, string field, float value)
        {
            var placement = rule.Placement ?? ReminderPlacements.Custom(0.5f, 0.5f, 0f, 0f);
            var x = placement.NormalizedX;
            var y = placement.NormalizedY;
            var px = placement.PixelOffsetX;
            var py = placement.PixelOffsetY;
            switch (field)
            {
                case "normalized-x": x = value; break;
                case "normalized-y": y = value; break;
                case "pixel-offset-x": px = value; break;
                case "pixel-offset-y": py = value; break;
                default: return;
            }
            SetCustomPlacementValue(rule, x, y, px, py);
        }

        private static float GetCustomPlacementComponent(UiReminderModel rule, string field, float fallback)
        {
            var placement = rule.Placement;
            if (placement == null) return fallback;
            switch (field)
            {
                case "normalized-x": return placement.NormalizedX;
                case "normalized-y": return placement.NormalizedY;
                case "pixel-offset-x": return placement.PixelOffsetX;
                case "pixel-offset-y": return placement.PixelOffsetY;
                default: return fallback;
            }
        }

        private void DrawReminderFrequency(UiReminderModel rule, bool narrow, UiTextCatalog text)
        {
            GUILayout.Space(2f);
            GUILayout.BeginHorizontal();
            DrawLabelWithInfoStatic(text.RepeatMode, text.RepeatModeHelp, 96f);
            var labels = new[]
            {
                text.ReminderRepeatMode(ReminderRepeatMode.Once),
                text.ReminderRepeatMode(ReminderRepeatMode.WhilePresent)
            };
            var selected = rule.RepeatMode == ReminderRepeatMode.WhilePresent ? 1 : 0;
            var columns = narrow ? 1 : 2;
            var newSelected = GUILayout.SelectionGrid(
                selected,
                labels,
                columns,
                narrow ? GUILayout.ExpandWidth(true) : GUILayout.Width(430f));
            if (newSelected != selected)
                rule.RepeatMode = newSelected == 1 ? ReminderRepeatMode.WhilePresent : ReminderRepeatMode.Once;
            GUILayout.EndHorizontal();

            if (rule.RepeatMode != ReminderRepeatMode.WhilePresent) return;

            GUILayout.BeginHorizontal();
            GUILayout.Label(text.Period, GUILayout.Width(narrow ? 150f : 96f));
            DrawNumericField(rule, "period", rule.PeriodSeconds.ToString("R", CultureInfo.InvariantCulture), 86f, periodText =>
            {
                if (double.TryParse(periodText, NumberStyles.Float, CultureInfo.InvariantCulture, out var period)
                    && NumericSafety.IsFinite(period))
                {
                    var normalized = ReminderRule.NormalizePeriod(period);
                    rule.PeriodSeconds = normalized;
                    rule.SendsPerPeriod = ReminderRule.NormalizeSends(normalized, rule.SendsPerPeriod);
                }
            });
            GUILayout.Label(text.Seconds, GUILayout.Width(36f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(text.SendsPerPeriod, GUILayout.Width(narrow ? 150f : 132f));
            DrawNumericField(rule, "sends", rule.SendsPerPeriod.ToString(CultureInfo.InvariantCulture), 56f, sendsText =>
            {
                if (int.TryParse(sendsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sends))
                    rule.SendsPerPeriod = ReminderRule.NormalizeSends(rule.PeriodSeconds, sends);
            });
            GUILayout.EndHorizontal();

            GUILayout.Label(string.Format(
                CultureInfo.CurrentCulture,
                text.ApproxInterval,
                rule.EffectiveIntervalSeconds));
        }

        private static void DrawLabelWithInfoStatic(string label, string help, float labelWidth)
        {
            GUILayout.Label(label, GUILayout.Width(labelWidth));
            DrawInfoButton(help);
        }

        private static void RemoveDescending<T>(List<T> items, List<int> indexes)
        {
            indexes.Sort();
            for (int i = indexes.Count - 1; i >= 0; i--)
                if (indexes[i] >= 0 && indexes[i] < items.Count)
                    items.RemoveAt(indexes[i]);
        }

        private void DrawSectionHeader(string title, string help)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, style, GUILayout.ExpandWidth(true), GUILayout.Height(24f));
            DrawInfoButton(help);
            GUILayout.EndHorizontal();
        }

        private void DrawLabelWithInfo(string label, string help, float labelWidth = 0f)
        {
            if (labelWidth > 0f) GUILayout.Label(label, GUILayout.Width(labelWidth));
            else GUILayout.Label(label, GUILayout.ExpandWidth(true));
            DrawInfoButton(help);
        }

        private static void DrawInfoButton(string help)
        {
            var style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            GUILayout.Label(new GUIContent("i", help ?? string.Empty), style, GUILayout.Width(22f), GUILayout.Height(22f));
        }

        /// <summary>
        /// Shared tooltip overlay, drawn ON TOP of all windows at the hovered window's
        /// top-left (virtual coordinates). Only the top-most pane under the mouse shows
        /// it; drawing inside every pane would duplicate the box.
        /// </summary>
        private void DrawTooltipOverlay(List<WindowPane> drawOrder)
        {
            if (string.IsNullOrWhiteSpace(GUI.tooltip)) return;
            var ev = Event.current;
            if (ev == null) return;
            var mouse = ev.mousePosition;
            WindowPane hovered = null;
            for (int i = 0; i < drawOrder.Count; i++)
            {
                if (drawOrder[i].Rect.Contains(mouse)) hovered = drawOrder[i];
            }
            if (hovered == null) return;
            var style = new GUIStyle(GUI.skin.box)
            {
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(10, 10, 8, 8)
            };
            var width = Mathf.Min(460f, Mathf.Max(220f, hovered.Rect.width - 24f));
            var content = new GUIContent(GUI.tooltip);
            var height = Mathf.Min(170f, style.CalcHeight(content, width));
            GUI.Box(new Rect(hovered.Rect.x + 12f, hovered.Rect.y + 32f, width, height), content, style);
        }

        private bool IsNarrowLayout()
        {
            var pane = _currentPane ?? _mainPane;
            return FallbackWindowGeometry.IsNarrowLayout(pane.Rect.width);
        }

        private void ConfigurePaneRect(WindowPane pane, bool center, float scale)
        {
            if (scale <= 0f) scale = CalculateScale(Screen.height);
            var virtualWidth = FallbackWindowGeometry.VirtualExtent(Screen.width, scale, FallbackWindowGeometry.FallbackVirtualWidth);
            var virtualHeight = FallbackWindowGeometry.VirtualExtent(Screen.height, scale, FallbackWindowGeometry.FallbackVirtualHeight);
            var r = FallbackWindowGeometry.ClampWindowRect(
                new RectF(pane.Rect.x, pane.Rect.y, pane.Rect.width, pane.Rect.height),
                virtualWidth, virtualHeight,
                pane.MinWidth, pane.MinHeight,
                virtualWidth * pane.MaxWidthRatio, virtualHeight * pane.MaxHeightRatio,
                center);
            pane.Rect = new Rect(r.X, r.Y, r.Width, r.Height);
        }

        private static float CalculateScale(int screenHeight)
        {
            return FallbackWindowGeometry.CalculateScale(screenHeight);
        }

        private static InGroupSortMode DrawInGroupSort(InGroupSortMode current, UiTextCatalog text)
        {
            // Explicit mapping keeps display order stable even if enum declaration changes.
            var names = new[]
            {
                text.InGroupSortOption(InGroupSortMode.IntensityDesc),
                text.InGroupSortOption(InGroupSortMode.IntensityAsc),
                text.InGroupSortOption(InGroupSortMode.RuleIndex)
            };
            int selected = current == InGroupSortMode.IntensityDesc ? 0
                : current == InGroupSortMode.IntensityAsc ? 1 : 2;
            selected = GUILayout.SelectionGrid(selected, names, names.Length, GUILayout.Height(24f));
            return selected == 0 ? InGroupSortMode.IntensityDesc
                : selected == 1 ? InGroupSortMode.IntensityAsc
                : InGroupSortMode.RuleIndex;
        }

        private static UnknownStatePolicy DrawPolicy(UnknownStatePolicy current, UiTextCatalog text)
        {
            // Explicit mapping keeps display order stable even if enum declaration changes.
            var names = new[]
            {
                text.UnknownPolicy(UnknownStatePolicy.Keep),
                text.UnknownPolicy(UnknownStatePolicy.End)
            };
            var selected = current == UnknownStatePolicy.End ? 1 : 0;
            selected = GUILayout.SelectionGrid(selected, names, names.Length, GUILayout.Height(24f));
            return selected == 1 ? UnknownStatePolicy.End : UnknownStatePolicy.Keep;
        }

        private static List<string> SplitList(string text)
        {
            var result = new List<string>();
            foreach (var item in (text ?? string.Empty).Split(','))
            {
                var value = item.Trim();
                if (value.Length > 0 && !result.Contains(value)) result.Add(value);
            }
            return result;
        }

    }
}
