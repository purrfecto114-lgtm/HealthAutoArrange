using System;

namespace HealthAutoArrange.Core
{
    /// <summary>
    /// Pure float rectangle used by <see cref="FallbackWindowGeometry"/> so the IMGUI
    /// window geometry is unit-testable from the Core-referencing test project
    /// (which cannot reference UnityEngine).
    /// </summary>
    public readonly struct RectF
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Width;
        public readonly float Height;

        public RectF(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }

    /// <summary>
    /// v1.3.0: the shared DPI/geometry math behind every fallback settings window.
    /// All windows scale with Screen.height / 1080 (clamped 1x-2x), clamp inside the
    /// virtual screen every frame (resolution hot-change self-healing), and share the
    /// narrow-layout threshold. Pure functions: no Unity dependency.
    /// </summary>
    public static class FallbackWindowGeometry
    {
        public const float DesignHeight = 1080f;
        public const float NarrowWidthThreshold = 600f;
        public const float FallbackVirtualWidth = 1280f;
        public const float FallbackVirtualHeight = 720f;

        /// <summary>Clamp(screenHeight / 1080, 1, 2); non-positive height falls back to 1x.</summary>
        public static float CalculateScale(int screenHeight)
        {
            if (screenHeight <= 0) return 1f;
            return Clamp(screenHeight / DesignHeight, 1f, 2f);
        }

        /// <summary>Screen extent in virtual (pre-matrix) pixels; degenerate sizes fall back.</summary>
        public static float VirtualExtent(int screenPixels, float scale, float fallback)
        {
            if (screenPixels > 0 && scale > 0f) return screenPixels / scale;
            return fallback;
        }

        /// <summary>
        /// Clamp a window rect into the virtual screen: size within [min, max]
        /// (min auto-shrinks below a too-small screen), then position either centered
        /// or clamped to [0, screen - size]. Idempotent: clamping a clamped rect is a
        /// no-op, which is what makes resolution hot-changes self-heal.
        /// </summary>
        public static RectF ClampWindowRect(
            RectF rect, float virtualWidth, float virtualHeight,
            float minWidth, float minHeight, float maxWidth, float maxHeight, bool center)
        {
            maxWidth = Max(1f, maxWidth);
            maxHeight = Max(1f, maxHeight);
            minWidth = Min(minWidth, maxWidth);
            minHeight = Min(minHeight, maxHeight);

            var width = Clamp(rect.Width, minWidth, maxWidth);
            var height = Clamp(rect.Height, minHeight, maxHeight);

            float x, y;
            if (center)
            {
                x = (virtualWidth - width) * 0.5f;
                y = (virtualHeight - height) * 0.5f;
            }
            else
            {
                x = Clamp(rect.X, 0f, Max(0f, virtualWidth - width));
                y = Clamp(rect.Y, 0f, Max(0f, virtualHeight - height));
            }
            return new RectF(x, y, width, height);
        }

        /// <summary>Narrow-layout switch (stacked controls) below this virtual width.</summary>
        public static bool IsNarrowLayout(float windowWidth)
        {
            return windowWidth < NarrowWidthThreshold;
        }

        /// <summary>
        /// Position for a freshly opened sub-window: cascaded down-right from the main
        /// window so it never covers the main header, then clamped on-screen.
        /// </summary>
        public static RectF CascadedRect(
            RectF anchor, int index, float width, float height,
            float virtualWidth, float virtualHeight, float minWidth, float minHeight,
            float maxWidth, float maxHeight)
        {
            var offset = 28f + 26f * index;
            return ClampWindowRect(
                new RectF(anchor.X + offset, anchor.Y + offset, width, height),
                virtualWidth, virtualHeight, minWidth, minHeight, maxWidth, maxHeight, false);
        }

        private static float Clamp(float v, float lo, float hi)
        {
            if (hi < lo) hi = lo;
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        private static float Min(float a, float b) => a < b ? a : b;

        private static float Max(float a, float b) => a > b ? a : b;
    }
}
