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

        // WhatsApp Desktop is LTR: the chat list is always the left panel.
        var chatListRect     = new Rect(0,      0, bestX,              frameHeight);
        var conversationRect = new Rect(bestX,  0, frameWidth - bestX, frameHeight);

        return new RegionDetectionResult
        {
            Succeeded        = true,
            ChatListRect     = chatListRect,
            ConversationRect = conversationRect,
            FailureReason    = string.Empty,
        };
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
