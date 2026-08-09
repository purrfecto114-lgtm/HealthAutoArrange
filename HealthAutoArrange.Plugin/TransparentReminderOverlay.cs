using System;
using System.Collections.Generic;
using HealthAutoArrange.Core;
using UnityEngine;

namespace HealthAutoArrange.Plugin
{
    /// <summary>
    /// 透明提醒绘制器。只使用 GUI.Box/GUI.Label，不创建 Window、不读取输入，
    /// 调用方应在 OnGUI 中隔离 Draw 的 Unity 异常。
    /// </summary>
    public sealed class TransparentReminderOverlay
    {
        private const float DesignHeight = 1080f;
        private readonly ReminderPresentation _presentation;
        private UiTextCatalog _text;

        public TransparentReminderOverlay(ReminderPresentation presentation)
            : this(presentation, Application.systemLanguage.ToString().StartsWith("Chinese", StringComparison.OrdinalIgnoreCase))
        {
        }

        public TransparentReminderOverlay(ReminderPresentation presentation, bool chinese)
        {
            _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
            _text = UiTextCatalog.ForLanguage(chinese);
        }

        public void SetLanguage(bool chinese)
        {
            _text = UiTextCatalog.ForLanguage(chinese);
        }

        /// <summary>当前系统使用的文案目录；提醒文本本身由 Presentation 模板生成。</summary>
        public UiTextCatalog Text => _text;

        public void Draw()
        {
            Draw(DateTimeOffset.UtcNow);
        }

        public void Draw(DateTimeOffset now)
        {
            var previousMatrix = GUI.matrix;
            var previousColor = GUI.color;
            var scale = CalculateScale(Screen.height);
            try
            {
                GUI.matrix = previousMatrix * Matrix4x4.Scale(new Vector3(scale, scale, 1f));
                var screenWidth = Screen.width > 0 ? Screen.width / scale : 1280f;
                var screenHeight = Screen.height > 0 ? Screen.height / scale : 720f;
                var active = _presentation.Active(now);
                var stackOffsets = new Dictionary<ReminderPlacementPreset, float>();
                for (int i = 0; i < active.Count; i++)
                {
                    var item = active[i];
                    var alpha = _presentation.Alpha(item, now);
                    if (alpha <= 0f || string.IsNullOrEmpty(item.Text)) continue;
                    var placement = item.Preset.Placement;
                    var placementKey = placement == null ? ReminderPlacementPreset.Bottom : placement.Preset;
                    stackOffsets.TryGetValue(placementKey, out var offset);
                    var height = DrawItem(item, alpha, offset, screenWidth, screenHeight);
                    stackOffsets[placementKey] = offset + height + 8f;
                }
            }
            finally
            {
                GUI.color = previousColor;
                GUI.matrix = previousMatrix;
            }
        }

        private static float DrawItem(
            ReminderPresentationItem item,
            float alpha,
            float stackOffset,
            float screenWidth,
            float screenHeight)
        {
            var preset = item.Preset;
            var width = Mathf.Min(preset.MaxWidth, screenWidth * 0.8f);
            var boxStyle = new GUIStyle(GUI.skin.box);
            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                wordWrap = true,
                fontSize = preset.FontSize,
                padding = new RectOffset(12, 12, 8, 8)
            };
            var height = Mathf.Max(32f, labelStyle.CalcHeight(new GUIContent(item.Text), width));
            var placement = preset.Placement;
            var x = placement.NormalizedX * screenWidth + placement.PixelOffsetX - width * 0.5f;
            var y = placement.NormalizedY * screenHeight + placement.PixelOffsetY;
            switch (placement.Preset)
            {
                case ReminderPlacementPreset.Bottom:
                case ReminderPlacementPreset.BottomLeft:
                    y -= height;
                    break;
                case ReminderPlacementPreset.Center:
                case ReminderPlacementPreset.Custom:
                    y -= height * 0.5f;
                    break;
            }
            if (placement.Preset == ReminderPlacementPreset.BottomLeft)
                x = placement.NormalizedX * screenWidth + placement.PixelOffsetX;
            y += StackOffset(placement.Preset, stackOffset);
            x = Mathf.Clamp(x, 4f, Mathf.Max(4f, screenWidth - width - 4f));
            y = Mathf.Clamp(y, 4f, Mathf.Max(4f, screenHeight - height - 4f));

            GUI.color = new Color(0.04f, 0.04f, 0.04f, Mathf.Clamp01(alpha * 0.72f));
            GUI.Box(new Rect(x, y, width, height), GUIContent.none, boxStyle);
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            GUI.Label(new Rect(x, y, width, height), item.Text, labelStyle);
            return height;
        }

        private static float StackOffset(ReminderPlacementPreset placement, float offset)
        {
            if (placement == ReminderPlacementPreset.Top) return offset;
            return -offset;
        }

        private static float CalculateScale(int screenHeight)
        {
            if (screenHeight <= 0) return 1f;
            return Mathf.Clamp(screenHeight / DesignHeight, 1f, 2f);
        }
    }
}
