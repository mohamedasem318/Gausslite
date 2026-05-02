using System.Windows;
using Gausslite.Core.Detection;

namespace Gausslite.Core.Tests.Detection;

public sealed class WhatsAppRegionDetectorTests
{
    private readonly WhatsAppRegionDetector _detector = new();

    private static byte[] BuildFrame(int width, int height, int stride,
        Func<int, int, (byte b, byte g, byte r)> color)
    {
        var buffer = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var (b, g, r) = color(x, y);
                int offset = y * stride + x * 4;
                buffer[offset]     = b;
                buffer[offset + 1] = g;
                buffer[offset + 2] = r;
                buffer[offset + 3] = 255;
            }
        }
        return buffer;
    }

    // Test 1: LTR layout — narrow chat list on the left, wide conversation on the right.
    [Fact]
    public void Detect_DividerAt300_Returns_CorrectRects()
    {
        const int W = 1280, H = 800, Stride = W * 4;
        var frame = BuildFrame(W, H, Stride, (x, _) =>
            x < 300 ? ((byte)20, (byte)20, (byte)20)
                     : ((byte)255, (byte)255, (byte)255));

        var result = _detector.Detect(frame, W, H, Stride);

        Assert.True(result.Succeeded);
        Assert.Equal(new Rect(0,   0, 300, H), result.ChatListRect);
        Assert.Equal(new Rect(300, 0, 980, H), result.ConversationRect);
    }

    // Test 2: Divider right of centre (wide chat list) — WhatsApp is LTR so the
    // left panel is always the chat list, regardless of which side is narrower.
    [Fact]
    public void Detect_DividerRightOfCenter_ChatListIsAlwaysLeftPanel()
    {
        const int W = 1280, H = 800, Stride = W * 4;
        var frame = BuildFrame(W, H, Stride, (x, _) =>
            x < 980 ? ((byte)255, (byte)255, (byte)255)
                     : ((byte)20, (byte)20, (byte)20));

        var result = _detector.Detect(frame, W, H, Stride);

        Assert.True(result.Succeeded);
        Assert.Equal(new Rect(0,   0, 980, H), result.ChatListRect);
        Assert.Equal(new Rect(980, 0, 300, H), result.ConversationRect);
    }

    // Test 3: All pixels the same colour — no divider to find.
    [Fact]
    public void Detect_UniformFrame_Fails_NoStrongEdge()
    {
        const int W = 1280, H = 800, Stride = W * 4;
        var frame = BuildFrame(W, H, Stride, (_, _) => ((byte)50, (byte)50, (byte)50));

        var result = _detector.Detect(frame, W, H, Stride);

        Assert.False(result.Succeeded);
        Assert.Contains("no strong edge", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    // Test 4: Strong edge at x=20, but that is outside the plausible 10-90% range.
    [Fact]
    public void Detect_DividerTooCloseToEdge_Fails_OutOfPlausibleRange()
    {
        const int W = 1280, H = 800, Stride = W * 4;
        var frame = BuildFrame(W, H, Stride, (x, _) =>
            x < 20 ? ((byte)20, (byte)20, (byte)20)
                    : ((byte)255, (byte)255, (byte)255));

        var result = _detector.Detect(frame, W, H, Stride);

        Assert.False(result.Succeeded);
        Assert.Contains("out of plausible range", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    // Test 5: Frame dimensions below the 200×200 minimum.
    [Fact]
    public void Detect_FrameTooSmall_Fails()
    {
        const int W = 100, H = 100, Stride = W * 4;
        var frame = new byte[Stride * H];

        var result = _detector.Detect(frame, W, H, Stride);

        Assert.False(result.Succeeded);
        Assert.Contains("frame too small", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    // Test 6: Row stride larger than width*4 (GPU row-pitch alignment padding).
    // stride=4096 > 800*4=3200, leaving 896 bytes of unused padding per row.
    // Note: the spec cites stride=4096 with a 1280-wide frame, but 1280*4=5120 > 4096,
    // so a width of 800 is used here to produce a valid "extra padding" scenario.
    [Fact]
    public void Detect_StrideLargerThanMinimum_ReadsCorrectly()
    {
        const int W = 800, H = 600, Stride = 4096; // 4096 > W*4=3200
        var frame = BuildFrame(W, H, Stride, (x, _) =>
            x < 250 ? ((byte)20, (byte)20, (byte)20)
                     : ((byte)255, (byte)255, (byte)255));

        var result = _detector.Detect(frame, W, H, Stride);

        Assert.True(result.Succeeded);
        Assert.Equal(new Rect(0,   0, 250, H), result.ChatListRect);
        Assert.Equal(new Rect(250, 0, 550, H), result.ConversationRect);
    }

    // Test 7: A "highlighted chat" row produces a high delta at y=H/2 only.
    // The three-row minimum-delta logic must prefer the real full-height divider
    // at x=300 over the transient spike at x=600/611.
    [Fact]
    public void Detect_ThreeRowConsensus_IgnoresTransientHighlight()
    {
        const int W = 1280, H = 800, Stride = W * 4;
        var frame = BuildFrame(W, H, Stride, (x, y) =>
        {
            // Bright yellow highlight spanning columns 600-610, centre row only.
            if (y == H / 2 && x >= 600 && x <= 610)
                return ((byte)0, (byte)255, (byte)255); // BGRA: B=0,G=255,R=255 = yellow

            return x < 300
                ? ((byte)20,  (byte)20,  (byte)20)   // dark  (chat list)
                : ((byte)255, (byte)255, (byte)255);  // white (conversation)
        });

        var result = _detector.Detect(frame, W, H, Stride);

        Assert.True(result.Succeeded);
        Assert.Equal(new Rect(0,   0, 300, H), result.ChatListRect);
        Assert.Equal(new Rect(300, 0, 980, H), result.ConversationRect);
    }
}
