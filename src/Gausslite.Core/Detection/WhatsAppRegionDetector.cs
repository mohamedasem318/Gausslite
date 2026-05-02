using System.Windows;

namespace Gausslite.Core.Detection;

public sealed class WhatsAppRegionDetector : IRegionDetector
{
    private const int    MinFrameSize          = 200;
    private const int    EdgeIgnorePixels      = 5;
    private const double PlausibleRangeMin     = 0.10;
    private const double PlausibleRangeMax     = 0.90;
    private const int    EdgeStrengthThreshold = 30;
    private const int    RowSampleStep         = 10;   // sample a row every N px
    private const double ConsistencyRequired   = 0.70; // fraction of sampled rows that must show the edge
    private const int    TitleBarIgnore       = 30;   // skip top N px (window controls live there)
    private const int    RailSearchMaxPx     = 200;  // absolute ceiling: walk at most 200 px from each edge
    private const double RailSearchCapFrac   = 0.40; // relative cap: never exceed 40 % of frame width
    private const double ColumnBusyFraction  = 0.25; // fraction of sampled rows that marks a column as content

    public RegionDetectionResult Detect(
        ReadOnlySpan<byte> bgraPixels,
        int frameWidth,
        int frameHeight,
        int frameStride)
    {
        if (frameStride < frameWidth * 4)
            throw new ArgumentException(
                "frameStride must be at least frameWidth * 4.",
                nameof(frameStride));

        if (bgraPixels.Length < frameStride * frameHeight)
            throw new ArgumentException(
                "bgraPixels is too short for the given frame dimensions.",
                nameof(bgraPixels));

        if (frameWidth < MinFrameSize || frameHeight < MinFrameSize)
            return Fail("frame too small");

        int totalSampled  = 0;
        for (int y = 0; y < frameHeight; y += RowSampleStep) totalSampled++;
        int minConsistent = (int)(totalSampled * ConsistencyRequired);

        int rangeMinPx = (int)(PlausibleRangeMin * frameWidth);
        int rangeMaxPx = (int)(PlausibleRangeMax * frameWidth);

        // Phase 1: best consistent-across-full-height edge within the plausible range.
        //
        // Searching only within the plausible range keeps the narrow navigation-icons
        // strip (< 10 % from the left edge in modern WhatsApp) from outscoring the
        // real panel divider. Scoring by row-count rather than minimum delta means a
        // tall in-chat image — whose border is strong at only a fraction of heights —
        // cannot beat the divider, which is present at every row.
        int bestX     = -1;
        int bestCount = -1;

        for (int x = Math.Max(EdgeIgnorePixels, rangeMinPx);
                 x <= Math.Min(rangeMaxPx, frameWidth - EdgeIgnorePixels - 1);
                 x++)
        {
            int count = CountConsistentRows(bgraPixels, frameStride, x, frameHeight);
            if (count > bestCount) { bestCount = count; bestX = x; }
        }

        if (bestX < 0 || bestCount < minConsistent)
        {
            // Phase 2: check outside the plausible range to give an informative reason.
            for (int x = EdgeIgnorePixels; x < frameWidth - EdgeIgnorePixels; x++)
            {
                if (x >= rangeMinPx && x <= rangeMaxPx) continue;
                if (CountConsistentRows(bgraPixels, frameStride, x, frameHeight) >= minConsistent)
                    return Fail("divider out of plausible range");
            }
            return Fail("no strong edge found");
        }

        var (railSide, leftWidth, rightWidth) =
            DetermineRailSide(bgraPixels, frameStride, frameWidth, frameHeight);

        Rect chatListRect, conversationRect;
        if (railSide == RailSide.Right)
        {
            // RTL layout: navigation rail is on the right → chat list is the right panel.
            chatListRect     = new Rect(bestX, 0, frameWidth - bestX, frameHeight);
            conversationRect = new Rect(0,     0, bestX,              frameHeight);
        }
        else
        {
            // LTR layout (default): navigation rail is on the left → chat list is the left panel.
            chatListRect     = new Rect(0,     0, bestX,              frameHeight);
            conversationRect = new Rect(bestX, 0, frameWidth - bestX, frameHeight);
        }

        return new RegionDetectionResult
        {
            Succeeded          = true,
            ChatListRect       = chatListRect,
            ConversationRect   = conversationRect,
            FailureReason      = string.Empty,
            DetectedRailSide  = railSide,
            RailSideLeftWidth  = leftWidth,
            RailSideRightWidth = rightWidth,
        };
    }

    // Determine which outer edge carries the navigation rail by measuring how far
    // inward from each edge the frame is vertically uniform (the rail's signature).
    //
    // The rail is a narrow (~50 px) solid-background column at the frame's outer edge.
    // Its columns are vertically uniform (row-to-row deltas ≈ 0).  Chat-list content
    // (contact rows, avatars, timestamps) follows within ~100 px of the edge, and is
    // detectable at the 25% row-activity threshold.  The conversation pane's outer
    // edge may also be quiet (empty background, no message bubbles near the frame
    // edge), but because it lacks a fixed chat-list boundary it often exhausts the
    // search range without triggering the busy threshold.
    //
    // maxSearch is a fixed 200-px ceiling (capped at 40% of frame width) so it
    // reaches the chat-list transition even on narrow windows where 10% of frame
    // width would fall short.
    //
    // Key invariant: default width is 0, not maxSearch.  "No transition found" means
    // no rail evidence on that side, NOT a wide quiet zone.  A genuine rail always
    // produces a detectable boundary (chat-list content) within RailSearchFraction;
    // a featureless background that exhausts the search must score 0 so it cannot
    // impersonate a wide rail and flip the result.
    //
    // Decision: larger non-zero width wins.  Both zero → LTR fallback (Left).
    private static (RailSide side, int leftWidth, int rightWidth) DetermineRailSide(
        ReadOnlySpan<byte> pixels, int stride, int frameWidth, int frameHeight)
    {
        // Search up to RailSearchMaxPx from each outer edge, but never more than 40% of
        // frame width so we stay well within each pane regardless of window size.
        int maxSearch = Math.Min(RailSearchMaxPx, (int)(RailSearchCapFrac * frameWidth));

        int effectiveSampled = 0;
        for (int y = TitleBarIgnore; y < frameHeight; y += RowSampleStep)
            effectiveSampled++;
        int busyThreshold = Math.Max(3, (int)(effectiveSampled * ColumnBusyFraction));

        // Default 0: "no transition found" means no rail evidence on this side.
        // The rail always has a chat-list boundary within maxSearch that trips busyThreshold;
        // a uniformly quiet background never does, so it must not impersonate a wide quiet zone.
        //
        // Start both walks from EdgeIgnorePixels (same guard as the divider scan) to avoid
        // triggering on sub-pixel window border artifacts at x=0 or x=frameWidth-1.
        int leftWidth = 0;
        for (int x = EdgeIgnorePixels; x < maxSearch; x++)
        {
            int count = 0;
            for (int y = TitleBarIgnore; y < frameHeight; y += RowSampleStep)
                if (RowDelta(pixels, stride, x, y) > EdgeStrengthThreshold)
                    count++;
            if (count >= busyThreshold) { leftWidth = x; break; }
        }

        int rightWidth = 0;
        for (int x = frameWidth - 1 - EdgeIgnorePixels; x >= frameWidth - maxSearch; x--)
        {
            int count = 0;
            for (int y = TitleBarIgnore; y < frameHeight; y += RowSampleStep)
                if (RowDelta(pixels, stride, x, y) > EdgeStrengthThreshold)
                    count++;
            if (count >= busyThreshold) { rightWidth = frameWidth - 1 - x; break; }
        }

        // The side with the wider quiet zone is the rail side.
        // Strict greater-than keeps ties (including both-zero) as LTR (Left).
        RailSide side = rightWidth > leftWidth ? RailSide.Right : RailSide.Left;
        return (side, leftWidth, rightWidth);
    }

    // Vertical-edge strength at (x, y): L1 sum of |B|+|G|+|R| deltas between
    // pixel (x, y-1) and pixel (x, y).  Measures how much a column's colour changes
    // from one row to the next — high value means the column is not vertically uniform.
    private static int RowDelta(ReadOnlySpan<byte> pixels, int stride, int x, int y)
    {
        int top    = (y - 1) * stride + x * 4;
        int bottom = y       * stride + x * 4;
        int db = Math.Abs(pixels[bottom]     - pixels[top]);
        int dg = Math.Abs(pixels[bottom + 1] - pixels[top + 1]);
        int dr = Math.Abs(pixels[bottom + 2] - pixels[top + 2]);
        return db + dg + dr;
    }

    private static int CountConsistentRows(
        ReadOnlySpan<byte> pixels, int stride, int x, int frameHeight)
    {
        int count = 0;
        for (int y = 0; y < frameHeight; y += RowSampleStep)
        {
            if (ColumnDelta(pixels, stride, x, y) > EdgeStrengthThreshold)
                count++;
        }
        return count;
    }

    private static int ColumnDelta(ReadOnlySpan<byte> pixels, int stride, int x, int y)
    {
        int left  = y * stride + (x - 1) * 4;
        int right = y * stride + x * 4;
        int db = Math.Abs(pixels[right]     - pixels[left]);
        int dg = Math.Abs(pixels[right + 1] - pixels[left + 1]);
        int dr = Math.Abs(pixels[right + 2] - pixels[left + 2]);
        return db + dg + dr;
    }

    private static RegionDetectionResult Fail(string reason) =>
        new RegionDetectionResult { Succeeded = false, FailureReason = reason };
}
