namespace Gausslite.Core.ScreenShare;

/// <summary>
/// Coarse state of "is the user actively screen-sharing right now?".
/// The detector emits a transition only when this value flips.
/// </summary>
public enum ScreenShareState
{
    /// <summary>No known share-control window is currently visible.</summary>
    Idle,

    /// <summary>At least one known share-control window is currently visible.</summary>
    Active,
}
