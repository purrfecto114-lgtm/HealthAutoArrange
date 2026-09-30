using HealthAutoArrange.Core;
using Xunit;

namespace HealthAutoArrange.Tests
{
    /// <summary>
    /// v1.3.0 multi-window GUI geometry: the shared DPI scale pipeline and the
    /// per-window rect clamping that every fallback settings pane uses. These lock
    /// the High-DPI adaptation matrix (720p - 4K, windowed 125/150/200%, degenerate
    /// sizes) as pure functions, mirroring the in-repo behavior previously verified
    /// by static reasoning in the 1.2.x audits.
    /// </summary>
    public class FallbackWindowGeometryTests
    {
        // ---- CalculateScale ----

        [Theory]
        [InlineData(-100)] [InlineData(0)]
        public void CalculateScale_DegenerateHeight_FallsBackTo1x(int screenHeight)
        {
            Assert.Equal(1f, FallbackWindowGeometry.CalculateScale(screenHeight));
        }

        [Theory]
        [InlineData(539)] [InlineData(1079)] [InlineData(1080)]
        public void CalculateScale_AtOrBelowDesignFloor_ClampsTo1x(int screenHeight)
        {
            Assert.Equal(1f, FallbackWindowGeometry.CalculateScale(screenHeight));
        }

        [Theory]
        [InlineData(1440, 1.3333334f)]
        [InlineData(2160, 2f)]
        [InlineData(4320, 2f)]
        public void CalculateScale_AboveFloor_ScalesAndCaps(int screenHeight, float expected)
        {
            Assert.Equal(expected, FallbackWindowGeometry.CalculateScale(screenHeight), 4);
        }

        // ---- VirtualExtent ----

        [Fact]
        public void VirtualExtent_DegeneratePixels_FallsBack()
        {
            Assert.Equal(1280f, FallbackWindowGeometry.VirtualExtent(0, 1f, 1280f));
            Assert.Equal(720f, FallbackWindowGeometry.VirtualExtent(-5, 1f, 720f));
        }

        [Fact]
        public void VirtualExtent_DividesByScale()
        {
            Assert.Equal(800f, FallbackWindowGeometry.VirtualExtent(800, 1f, 1280f));
            Assert.Equal(1920f, FallbackWindowGeometry.VirtualExtent(3840, 2f, 1280f));
            Assert.Equal(675f, FallbackWindowGeometry.VirtualExtent(800, 1.1851852f, 1280f), 3);
        }

        [Fact]
        public void VirtualExtent_ZeroScale_FallsBack()
        {
            Assert.Equal(1280f, FallbackWindowGeometry.VirtualExtent(1920, 0f, 1280f));
        }

        // ---- ClampWindowRect ----

        [Fact]
        public void ClampWindowRect_ClampsSizeIntoMinMax()
        {
            var r = FallbackWindowGeometry.ClampWindowRect(new RectF(-10f, -10f, 9999f, 9999f),
                1280f, 720f, 460f, 380f, 1280f * 0.70f, 720f * 0.80f, false);
            Assert.Equal(1280f * 0.70f, r.Width, 3);   // max wins for oversized rects
            Assert.Equal(720f * 0.80f, r.Height, 3);
        }

        [Fact]
        public void ClampWindowRect_MinAutoShrinksBelowMax()
        {
            // Degenerate 640x360 virtual screen: main pane max is 448x288, so the
            // 460x380 min must auto-shrink to the max (never larger than the screen).
            var r = FallbackWindowGeometry.ClampWindowRect(new RectF(0f, 0f, 640f, 360f),
                640f, 360f, 460f, 380f, 640f * 0.70f, 360f * 0.80f, false);
            Assert.Equal(448f, r.Width);
            Assert.Equal(288f, r.Height);
        }

        [Fact]
        public void ClampWindowRect_PositionClampedInsideScreen()
        {
            var r = FallbackWindowGeometry.ClampWindowRect(new RectF(5000f, -50f, 640f, 560f),
                800f, 600f, 460f, 380f, 560f, 480f, false);
            Assert.True(r.X >= 0f && r.X <= 800f - r.Width);
            Assert.True(r.Y >= 0f && r.Y <= 600f - r.Height);
            Assert.Equal(0f, r.Y);
        }

        [Fact]
        public void ClampWindowRect_CentersWhenRequested()
        {
            var r = FallbackWindowGeometry.ClampWindowRect(new RectF(0f, 0f, 640f, 560f),
                1920f, 1080f, 460f, 380f, 1920f * 0.70f, 1080f * 0.80f, true);
            Assert.Equal((1920f - r.Width) * 0.5f, r.X, 3);
            Assert.Equal((1080f - r.Height) * 0.5f, r.Y, 3);
        }

        [Fact]
        public void ClampWindowRect_IsIdempotent_ResolutionHotChange()
        {
            // Dragged-out rect on a 1080p screen, then the resolution drops to 800x600:
            // after two clamps (once per frame) the rect is stable and fully on-screen.
            var dragged = new RectF(1500f, 900f, 640f, 560f);
            var vw = 800f; var vh = 600f;
            var once = FallbackWindowGeometry.ClampWindowRect(dragged, vw, vh, 460f, 380f, vw * 0.70f, vh * 0.80f, false);
            var twice = FallbackWindowGeometry.ClampWindowRect(once, vw, vh, 460f, 380f, vw * 0.70f, vh * 0.80f, false);
            Assert.Equal(once.X, twice.X, 5);
            Assert.Equal(once.Y, twice.Y, 5);
            Assert.Equal(once.Width, twice.Width, 5);
            Assert.Equal(once.Height, twice.Height, 5);
        }

        // ---- IsNarrowLayout ----

        [Theory]
        [InlineData(599.9f, true)]
        [InlineData(600f, false)]
        [InlineData(800f, false)]
        public void IsNarrowLayout_Threshold(float width, bool expected)
        {
            Assert.Equal(expected, FallbackWindowGeometry.IsNarrowLayout(width));
        }

        // ---- CascadedRect ----

        [Fact]
        public void CascadedRect_StaysOnScreen_AndClearsMainHeader()
        {
            var anchor = new RectF(0f, 0f, 640f, 560f);
            for (int index = 0; index < 5; index++)
            {
                var r = FallbackWindowGeometry.CascadedRect(anchor, index, 560f, 520f,
                    800f, 600f, 460f, 380f, 560f, 480f);
                Assert.True(r.X >= 0f && r.X <= 800f - r.Width);
                Assert.True(r.Y >= 0f && r.Y <= 600f - r.Height);
                Assert.True(r.X >= anchor.X + 27f, "cascade must clear the main window's 24px drag bar");
                Assert.True(r.Y >= anchor.Y + 27f);
            }
        }

        [Fact]
        public void CascadedRect_OffsetGrowsWithIndex()
        {
            var anchor = new RectF(100f, 100f, 640f, 560f);
            var a = FallbackWindowGeometry.CascadedRect(anchor, 0, 560f, 520f, 1920f, 1080f, 460f, 380f, 1344f, 864f);
            var b = FallbackWindowGeometry.CascadedRect(anchor, 1, 560f, 520f, 1920f, 1080f, 460f, 380f, 1344f, 864f);
            Assert.Equal(26f, b.X - a.X, 3);
            Assert.Equal(26f, b.Y - a.Y, 3);
        }

        // ---- per-window defaults fit their own minimums across the matrix ----

        public static TheoryData<string, float, float, float, float, float, float, float, float> PaneSpecs
            = new TheoryData<string, float, float, float, float, float, float, float, float>
        {
            // name, defaultW, defaultH, minW, minH, maxWRatio, maxHRatio, screenW, screenH
            { "Main@800x600",     640f, 560f, 460f, 380f, 0.70f, 0.80f, 800f,  600f },
            { "Main@1080p",       640f, 560f, 460f, 380f, 0.70f, 0.80f, 1920f, 1080f },
            { "Main@4K(2x)",      640f, 560f, 460f, 380f, 0.70f, 0.80f, 1920f, 1080f },
            { "Rules@800x600",    560f, 520f, 460f, 380f, 0.70f, 0.80f, 800f,  600f },
            { "Rules@1080p",      560f, 520f, 460f, 380f, 0.70f, 0.80f, 1920f, 1080f },
            { "Reminders@800x600",520f, 560f, 440f, 360f, 0.60f, 0.80f, 800f,  600f },
            { "Reminders@1080p", 520f, 560f, 440f, 360f, 0.60f, 0.80f, 1920f, 1080f },
            { "Updates@640x360",  460f, 360f, 360f, 240f, 0.50f, 0.60f, 640f,  360f },
            { "Updates@1080p",    460f, 360f, 360f, 240f, 0.50f, 0.60f, 1920f, 1080f },
        };

        [Theory]
        [MemberData(nameof(PaneSpecs))]
        public void PaneDefault_FitsScreenAndMinimums(string name, float defaultW, float defaultH,
            float minW, float minH, float maxWRatio, float maxHRatio, float screenW, float screenH)
        {
            var r = FallbackWindowGeometry.ClampWindowRect(new RectF(80f, 80f, defaultW, defaultH),
                screenW, screenH, minW, minH, screenW * maxWRatio, screenH * maxHRatio, false);
            Assert.True(r.Width <= screenW * maxWRatio + 0.01f, $"{name}: width {r.Width}");
            Assert.True(r.Height <= screenH * maxHRatio + 0.01f, $"{name}: height {r.Height}");
            Assert.True(r.Width >= System.Math.Min(minW, screenW * maxWRatio) - 0.01f);
            Assert.True(r.Height >= System.Math.Min(minH, screenH * maxHRatio) - 0.01f);
            Assert.True(r.X >= 0f && r.Y >= 0f);
            Assert.True(r.X + r.Width <= screenW + 0.01f);
            Assert.True(r.Y + r.Height <= screenH + 0.01f);
        }
    }
}
