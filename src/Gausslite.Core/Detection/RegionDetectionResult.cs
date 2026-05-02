using System.Windows;

namespace Gausslite.Core.Detection;

public readonly struct RegionDetectionResult
{
    public bool Succeeded { get; init; }
    public Rect ChatListRect { get; init; }
    public Rect ConversationRect { get; init; }
    public string FailureReason { get; init; }
    // Which outer edge the navigation rail was detected on.
    // Defaults to Left (LTR) when detection confidence is low or detection failed.
    public RailSide DetectedRailSide { get; init; }
    // How many columns from the left/right outer edge were "quiet" (vertically uniform)
    // before the first content column was found. The side with the larger value is the
    // detected rail side. 0 means content started at the very first column searched.
    public int RailSideLeftWidth  { get; init; }
    public int RailSideRightWidth { get; init; }
}
