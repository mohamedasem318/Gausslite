// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Windows;

namespace Gausslite.Core.Detection;

/// <summary>
/// Converts capture-content pixel rects into overlay-local DIP rects.
/// </summary>
/// <remarks>
/// Frame pixel (0,0) maps to overlay-local DIP (0,0) — no origin offset.
/// The 14×7 px structural gap between GetWindowRect and WGC content size is absorbed
/// into the ratio; callers do not need to account for it separately.
/// </remarks>
internal static class CaptureToOverlayConverter
{
    /// <summary>
    /// Converts a rectangle in capture-content pixels to overlay-local device-independent pixels.
    /// Returns <see cref="Rect.Empty"/> when either dimension is non-positive.
    /// </summary>
    internal static Rect Convert(
        Rect captureRect,
        int contentWidth,
        int contentHeight,
        double overlayWidth,
        double overlayHeight)
    {
        if (contentWidth <= 0 || contentHeight <= 0) return Rect.Empty;
        if (overlayWidth <= 0 || overlayHeight <= 0) return Rect.Empty;

        double scaleX = overlayWidth  / contentWidth;
        double scaleY = overlayHeight / contentHeight;

        return new Rect(
            captureRect.X      * scaleX,
            captureRect.Y      * scaleY,
            captureRect.Width  * scaleX,
            captureRect.Height * scaleY);
    }
}
