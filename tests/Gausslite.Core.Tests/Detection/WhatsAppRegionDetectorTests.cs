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

    // Test 2: Divider right of centre with no rail signal on either edge. Both strips
    // are uniform, so rail scores tie at zero — the LTR fallback assigns the left panel
    // as chat list regardless of which side is narrower.
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

    // Test 8: LTR layout — narrow uniform rail on the left, content on both sides.
    // x=0..49: uniform dark background (the rail — no row-to-row variation).
    // x=50..399: row-varying content (chat list).
    // x=400..1279: row-varying content with different range (conversation).
    // Divider at x=400.  Rail detection walks left edge, finds 50 quiet columns,
    // walks right edge, finds 0 quiet columns → Left wins → ChatListRect.X == 0.
    [Fact]
    public void Detect_RailOnLeft_AssignsChatListToLeft()
    {
        const int W = 1280, H = 800, Stride = W * 4;
        var frame = BuildFrame(W, H, Stride, (x, y) =>
        {
            if (x < 50)  // rail: vertically uniform
                return ((byte)30, (byte)30, (byte)30);
            if (x < 400) // chat list: content varies every 10 rows
            {
                byte v = (y / 10) % 2 == 0 ? (byte)20 : (byte)70;
                return (v, v, v);
            }
            // conversation: content varies every 10 rows, different brightness range
            {
                byte v = (y / 10) % 2 == 0 ? (byte)180 : (byte)230;
                return (v, v, v);
            }
        });

        var result = _detector.Detect(frame, W, H, Stride);

        Assert.True(result.Succeeded);
        Assert.Equal(0.0, result.ChatListRect.X);
        Assert.Equal(RailSide.Left, result.DetectedRailSide);
        Assert.True(result.RailSideLeftWidth > result.RailSideRightWidth,
            $"Expected leftWidth ({result.RailSideLeftWidth}) > rightWidth ({result.RailSideRightWidth})");
    }

    // Test 9: RTL layout — narrow uniform rail on the right.
    // x=0..399: row-varying content (conversation).
    // x=400..1229: row-varying content (chat list).
    // x=1230..1279: uniform dark background (the rail — 50 px).
    // Divider at x=400.  Rail detection: right edge walks 50 quiet columns,
    // left edge finds 0 quiet columns → Right wins → ChatListRect.X == 400.
    [Fact]
    public void Detect_RailOnRight_AssignsChatListToRight()
    {
        const int W = 1280, H = 800, Stride = W * 4;
        var frame = BuildFrame(W, H, Stride, (x, y) =>
        {
            if (x >= 1230) // rail: vertically uniform
                return ((byte)30, (byte)30, (byte)30);
            if (x >= 400)  // chat list: content varies every 10 rows
            {
                byte v = (y / 10) % 2 == 0 ? (byte)20 : (byte)70;
                return (v, v, v);
            }
            // conversation: content varies every 10 rows, different brightness range
            {
                byte v = (y / 10) % 2 == 0 ? (byte)180 : (byte)230;
                return (v, v, v);
            }
        });

        var result = _detector.Detect(frame, W, H, Stride);

        Assert.True(result.Succeeded);
        Assert.Equal(new Rect(400, 0, 880, H), result.ChatListRect);
        Assert.Equal(new Rect(0,   0, 400, H), result.ConversationRect);
        Assert.Equal(RailSide.Right, result.DetectedRailSide);
        Assert.True(result.RailSideRightWidth > result.RailSideLeftWidth,
            $"Expected rightWidth ({result.RailSideRightWidth}) > leftWidth ({result.RailSideLeftWidth})");
    }

    // Test 10: No quiet zone on either edge — content starts immediately on both sides.
    // Both widths are zero; detector falls back to LTR (chat list on left).
    [Fact]
    public void Detect_NoRailSignalEitherSide_DefaultsToLeft()
    {
        const int W = 1280, H = 800, Stride = W * 4;
        // Both edges have row-varying content immediately — no quiet zone.
        var frame = BuildFrame(W, H, Stride, (x, y) =>
        {
            byte v = (y / 10) % 2 == 0 ? (byte)20 : (byte)70;
            if (x < 640)
                return (v, v, v);
            byte v2 = (y / 10) % 2 == 0 ? (byte)180 : (byte)230;
            return (v2, v2, v2);
        });

        var result = _detector.Detect(frame, W, H, Stride);

        Assert.True(result.Succeeded);
        Assert.Equal(0.0, result.ChatListRect.X);
        Assert.Equal(RailSide.Left, result.DetectedRailSide);
        // Tie: both widths are equal (both sides hit content at the same depth),
        // so neither side beats the other and the LTR fallback applies.
        Assert.Equal(result.RailSideLeftWidth, result.RailSideRightWidth);
    }

    // Test 11: Narrow uniform scrollbar strip at the right outer edge should not defeat
    // a proper 50-px rail on the left.  The scrollbar is vertically uniform (same colour
    // top to bottom) so it looks quiet, but it is only ~5 px wide vs ~50 px for the rail.
    [Fact]
    public void Detect_RailOnLeft_WithRightScrollbar_StillAssignsChatListToLeft()
    {
        const int W = 1280, H = 800, Stride = W * 4;
        var frame = BuildFrame(W, H, Stride, (x, y) =>
        {
            if (x < 50)       // rail: uniform dark
                return ((byte)30, (byte)30, (byte)30);
            if (x >= 1275)    // scrollbar: uniform dark strip, only 5 px wide
                return ((byte)50, (byte)50, (byte)50);
            if (x < 640)      // chat list content
            {
                byte v = (y / 10) % 2 == 0 ? (byte)20 : (byte)70;
                return (v, v, v);
            }
            // conversation content
            {
                byte v = (y / 10) % 2 == 0 ? (byte)180 : (byte)230;
                return (v, v, v);
            }
        });

        var result = _detector.Detect(frame, W, H, Stride);

        Assert.True(result.Succeeded);
        Assert.Equal(0.0, result.ChatListRect.X);
        // Rail width on left (~50) must exceed scrollbar width on right (~5).
        Assert.True(result.RailSideLeftWidth > result.RailSideRightWidth,
            $"leftWidth={result.RailSideLeftWidth}, rightWidth={result.RailSideRightWidth}");
    }

    // Test 12: Leftmost edge wins even when a rightward edge has a higher consistent-row score.
    //
    // Regression for the maximize bug: when WhatsApp is maximized with the chat list still
    // at its pre-maximize width (~451 px), the message-bubble max-width column (~750 px
    // into the conversation pane, i.e. x≈1201 in a 1920-px frame) produces a secondary
    // vertical edge whose consistent-row count can exceed the real divider. The old
    // global-maximum rule picked the secondary edge; the new leftmost-above-threshold
    // rule must pick the real divider.
    //
    // Frame geometry (1920×1032, mimics the maximize bug):
    //   x=0..450    dark  — chat list
    //   x=451..1200 medium — conversation (edge at x=451 suppressed on 25% of rows → 77/103 consistent)
    //   x=1201..    light  — secondary panel (edge at x=1201 present on all 103 rows)
    // minConsistent = ⌊103 × 0.70⌋ = 72. Both edges qualify; leftmost must win.
    [Fact]
    public void Detect_LeftmostEdge_Wins_WhenSecondaryEdgeHasHigherScore()
    {
        const int W = 1920, H = 1032, Stride = W * 4;

        // Edge at x=451 is suppressed on rows where (y/10) % 4 == 0 (26 of 103 sampled rows).
        // Those rows get the same medium-gray pixel on both sides so ColumnDelta=0 there.
        // Resulting count at x=451: 103 − 26 = 77 ≥ 72 = minConsistent — qualifies.
        // Count at x=1201: 103 — outscores x=451 under the old global-max rule.
        var frame = BuildFrame(W, H, Stride, (x, y) =>
        {
            if (x < 450)
                return ((byte)30, (byte)30, (byte)30);   // dark — chat list interior
            if (x == 450)
            {
                // Left side of the real divider: suppress the edge on 25% of sampled rows
                // so the secondary edge at x=1201 scores higher in absolute row count.
                int sampleIndex = y / 10;
                return sampleIndex % 4 == 0
                    ? ((byte)120, (byte)120, (byte)120)  // same as conversation — no edge here
                    : ((byte)30,  (byte)30,  (byte)30);  // dark — edge present
            }
            if (x < 1201)
                return ((byte)120, (byte)120, (byte)120); // medium — conversation
            return ((byte)220, (byte)220, (byte)220);     // light  — secondary panel
        });

        var result = _detector.Detect(frame, W, H, Stride);

        Assert.True(result.Succeeded);
        // Real divider is at x=451 (leftmost qualifying edge), not x=1201.
        Assert.Equal(451.0, result.ChatListRect.Width, precision: 0);
        Assert.Equal(0.0,   result.ChatListRect.X,     precision: 0);
    }

    // Test 13: Maximized layout regression — three-panel frame, LTR, rail on left.
    // Verifies the same leftmost-wins property while also exercising rail-side
    // detection, confirming the chat list is correctly assigned to the left panel.
    //
    // Key: the mixed/suppressed column is at x=451 (first conversation column), not
    // x=450 (last chat-list column).  Columns inside the chat-list region have the same
    // y-dependent colour on both sides so ColumnDelta=0 there; only at x=451 does the
    // boundary appear, with 25% of rows suppressed to make the secondary edge stronger.
    [Fact]
    public void Detect_MaximizedLayout_LTRRail_LeftmostDividerWins()
    {
        const int W = 1920, H = 1032, Stride = W * 4;

        var frame = BuildFrame(W, H, Stride, (x, y) =>
        {
            // Uniform navigation rail (40 px) on the far left.
            if (x < 40)
                return ((byte)28, (byte)28, (byte)28);

            // Chat list: content varies every 10 rows (needed for rail detection).
            if (x <= 450)
            {
                byte v = (y / 10) % 2 == 0 ? (byte)22 : (byte)68;
                return (v, v, v);
            }

            // Real divider is the boundary between x=450 (chat list) and x=451 (conversation).
            // Suppress the edge on rows where (y/10) % 4 == 0 by returning the same dark value
            // as the chat-list colour on those rows (even sampleIndex → v=22).
            // ColumnDelta(x=451) checks pixel[451] vs pixel[450]:
            //   - suppressed rows: both 22 → delta=0 (26 of 103 rows)
            //   - non-suppressed:  118 vs 22 → delta=288 > 30  (77 of 103 rows ≥ minConsistent=72)
            if (x == 451)
            {
                int si = y / 10;
                return si % 4 == 0
                    ? ((byte)22,  (byte)22,  (byte)22)    // match chat-list → no edge
                    : ((byte)118, (byte)118, (byte)118);   // medium → edge present
            }

            // Conversation pane (x=452..1200): medium gray.
            if (x < 1201)
                return ((byte)118, (byte)118, (byte)118);

            // Secondary panel (x=1201+): varies → strong edge at x=1201 (100% rows, beats 77).
            byte v2 = (y / 10) % 2 == 0 ? (byte)200 : (byte)240;
            return (v2, v2, v2);
        });

        var result = _detector.Detect(frame, W, H, Stride);

        Assert.True(result.Succeeded);
        Assert.Equal(RailSide.Left, result.DetectedRailSide);
        // Real divider is at x=451 (leftmost qualifying edge), not x=1201.
        Assert.Equal(0.0,   result.ChatListRect.X,     precision: 0);
        Assert.Equal(451.0, result.ChatListRect.Width,  precision: 0);
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
