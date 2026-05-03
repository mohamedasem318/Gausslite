using System.Windows;
using Gausslite.Core.Detection;

namespace Gausslite.Core.Tests.Detection;

public sealed class CaptureToOverlayConverterTests
{
    private const double Delta = 0.0001;

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_WhenContentEqualsOverlay_ReturnsIdentical()
    {
        var rect   = new Rect(100, 50, 200, 300);
        var result = CaptureToOverlayConverter.Convert(rect, 800, 600, 800, 600);

        Assert.Equal(rect.X,      result.X,      Delta);
        Assert.Equal(rect.Y,      result.Y,      Delta);
        Assert.Equal(rect.Width,  result.Width,  Delta);
        Assert.Equal(rect.Height, result.Height, Delta);
    }

    // ── Ratio math ───────────────────────────────────────────────────────────

    [Fact]
    public void Convert_ScalesXAndYIndependently()
    {
        // overlay 400×300, content 800×600  →  scale 0.5 × 0.5
        var rect   = new Rect(200, 150, 400, 300);
        var result = CaptureToOverlayConverter.Convert(rect, 800, 600, 400, 300);

        Assert.Equal(100,  result.X,      Delta);
        Assert.Equal(75,   result.Y,      Delta);
        Assert.Equal(200,  result.Width,  Delta);
        Assert.Equal(150,  result.Height, Delta);
    }

    [Fact]
    public void Convert_NonUniformScale_ScalesAxesIndependently()
    {
        // overlay 800×600, content 1600×1200  →  scaleX=0.5, scaleY=0.5
        // Different non-uniform: overlay 400×600, content 800×600  →  scaleX=0.5, scaleY=1.0
        var rect   = new Rect(80, 60, 400, 300);
        var result = CaptureToOverlayConverter.Convert(rect, 800, 600, 400, 600);

        Assert.Equal(40,   result.X,      Delta);  // 80 * 0.5
        Assert.Equal(60,   result.Y,      Delta);  // 60 * 1.0
        Assert.Equal(200,  result.Width,  Delta);  // 400 * 0.5
        Assert.Equal(300,  result.Height, Delta);  // 300 * 1.0
    }

    // ── 14×7 px WGC structural gap ───────────────────────────────────────────

    [Fact]
    public void Convert_WithTypical14x7Gap_RatioIsSlightlyAboveOne()
    {
        // WGC content = 814×607 px, overlay DIP = 800×600  →  ratio slightly < 1
        // A full-width capture rect should map to slightly less than the overlay width.
        var rect   = new Rect(0, 0, 814, 607);
        var result = CaptureToOverlayConverter.Convert(rect, 814, 607, 800, 600);

        Assert.Equal(0,   result.X,      Delta);
        Assert.Equal(0,   result.Y,      Delta);
        Assert.Equal(800, result.Width,  Delta);
        Assert.Equal(600, result.Height, Delta);
    }

    [Fact]
    public void Convert_WithGap_HalfWidthRectScalesCorrectly()
    {
        // Capture content = 814×607, overlay = 800×600
        // A chat-list rect spanning the left half of the capture frame.
        var rect   = new Rect(0, 0, 200, 607);
        var result = CaptureToOverlayConverter.Convert(rect, 814, 607, 800, 600);

        double expectedW = 200.0 * (800.0 / 814.0);
        Assert.Equal(0,        result.X,      Delta);
        Assert.Equal(0,        result.Y,      Delta);
        Assert.Equal(expectedW, result.Width, Delta);
        Assert.Equal(600,      result.Height, Delta);
    }

    // ── Origin offset ────────────────────────────────────────────────────────

    [Fact]
    public void Convert_NonZeroOrigin_ScalesOriginByRatio()
    {
        // Rect starting at (200, 100) in 800×600 content, overlay is 400×300.
        var rect   = new Rect(200, 100, 100, 50);
        var result = CaptureToOverlayConverter.Convert(rect, 800, 600, 400, 300);

        Assert.Equal(100,  result.X,      Delta);  // 200 * 0.5
        Assert.Equal(50,   result.Y,      Delta);  // 100 * 0.5
        Assert.Equal(50,   result.Width,  Delta);  // 100 * 0.5
        Assert.Equal(25,   result.Height, Delta);  // 50  * 0.5
    }

    // ── Degenerate inputs ────────────────────────────────────────────────────

    [Fact]
    public void Convert_ZeroContentWidth_ReturnsEmpty()
    {
        var result = CaptureToOverlayConverter.Convert(new Rect(0, 0, 100, 100), 0, 600, 800, 600);
        Assert.Equal(Rect.Empty, result);
    }

    [Fact]
    public void Convert_ZeroContentHeight_ReturnsEmpty()
    {
        var result = CaptureToOverlayConverter.Convert(new Rect(0, 0, 100, 100), 800, 0, 800, 600);
        Assert.Equal(Rect.Empty, result);
    }

    [Fact]
    public void Convert_ZeroOverlayWidth_ReturnsEmpty()
    {
        var result = CaptureToOverlayConverter.Convert(new Rect(0, 0, 100, 100), 800, 600, 0, 600);
        Assert.Equal(Rect.Empty, result);
    }

    [Fact]
    public void Convert_ZeroOverlayHeight_ReturnsEmpty()
    {
        var result = CaptureToOverlayConverter.Convert(new Rect(0, 0, 100, 100), 800, 600, 800, 0);
        Assert.Equal(Rect.Empty, result);
    }

    [Fact]
    public void Convert_NegativeContentDimensions_ReturnsEmpty()
    {
        var result = CaptureToOverlayConverter.Convert(new Rect(0, 0, 100, 100), -1, 600, 800, 600);
        Assert.Equal(Rect.Empty, result);
    }
}
