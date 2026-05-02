using System.Windows;

namespace Gausslite.Core.Detection;

public readonly struct RegionDetectionResult
{
    public bool Succeeded { get; init; }
    public Rect ChatListRect { get; init; }
    public Rect ConversationRect { get; init; }
    public string FailureReason { get; init; }
}
