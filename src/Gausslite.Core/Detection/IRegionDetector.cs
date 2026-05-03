// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Gausslite.Core.Detection;

public interface IRegionDetector
{
    /// <summary>
    /// Analyzes a captured BGRA8 frame and returns the bounding rectangles of the
    /// detected sub-regions (e.g. chat list and conversation pane) in frame pixel coordinates.
    /// </summary>
    RegionDetectionResult Detect(
        ReadOnlySpan<byte> bgraPixels,
        int frameWidth,
        int frameHeight,
        int frameStride);
}
