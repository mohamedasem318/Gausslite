using System.Windows;

namespace Gausslite.Core.Detection;

public sealed class WhatsAppRegionDetector : IRegionDetector
{
    private const int MinFrameSize = 200;
    private const int EdgeIgnorePixels = 5;
    private const double PlausibleRangeMin = 0.10;
    private const double PlausibleRangeMax = 0.90;
    private const int EdgeStrengthThreshold = 30;

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

        int y1 = frameHeight / 4;
        int y2 = frameHeight / 2;
        int y3 = frameHeight * 3 / 4;

        int bestX = -1;
        int bestMinDelta = -1;

        for (int x = 1; x < frameWidth; x++)
        {
            // Ignore candidates within EdgeIgnorePixels of either edge (likely window border).
            if (x < EdgeIgnorePixels || x >= frameWidth - EdgeIgnorePixels)
                continue;

            int d1 = ColumnDelta(bgraPixels, frameStride, x, y1);
            int d2 = ColumnDelta(bgraPixels, frameStride, x, y2);
            int d3 = ColumnDelta(bgraPixels, frameStride, x, y3);

            // A real full-height divider is strong at all three rows; transient UI
            // elements (highlighted chat, hover state) spike on at most one row.
            int minDelta = Math.Min(d1, Math.Min(d2, d3));

            if (minDelta > bestMinDelta)
            {
                bestMinDelta = minDelta;
                bestX = x;
            }
        }

        if (bestX < 0 || bestMinDelta < EdgeStrengthThreshold)
            return Fail("no strong edge found");

        double rangeMin = PlausibleRangeMin * frameWidth;
        double rangeMax = PlausibleRangeMax * frameWidth;
        if (bestX < rangeMin || bestX > rangeMax)
            return Fail("divider out of plausible range");

        int leftWidth = bestX;
        int rightWidth = frameWidth - bestX;

        Rect chatListRect, conversationRect;
        if (leftWidth <= rightWidth)
        {
            chatListRect    = new Rect(0,      0, leftWidth,  frameHeight);
            conversationRect = new Rect(bestX, 0, rightWidth, frameHeight);
        }
        else
        {
            chatListRect    = new Rect(bestX, 0, rightWidth, frameHeight);
            conversationRect = new Rect(0,    0, leftWidth,  frameHeight);
        }

        return new RegionDetectionResult
        {
            Succeeded        = true,
            ChatListRect     = chatListRect,
            ConversationRect = conversationRect,
            FailureReason    = string.Empty,
        };
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
