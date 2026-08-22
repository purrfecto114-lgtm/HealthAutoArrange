using System;

namespace HealthAutoArrange.Core
{
    /// <summary>Small finite-number guard for user-editable visual settings.</summary>
    public static class NumericSafety
    {
        public static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public static float ClampFinite(float value, float min, float max, float fallback)
        {
            if (!IsFinite(value)) value = fallback;
            if (!IsFinite(fallback)) fallback = min;
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
