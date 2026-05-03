// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Gausslite.Core.ScreenShare;

/// <summary>
/// Polls top-level windows on a fixed cadence and reports whether any well-known
/// "share-control" window (e.g. Zoom's floating toolbar, Teams' "Sharing control bar")
/// is currently visible.  Treat the result as evidence of an *active* screen share —
/// not just "the app is running".
/// </summary>
public interface IScreenShareDetector : IDisposable
{
    /// <summary>
    /// Fires on the WPF UI thread whenever <see cref="CurrentState"/> flips between
    /// <see cref="ScreenShareState.Idle"/> and <see cref="ScreenShareState.Active"/>.
    /// Does NOT fire on every poll while the state is stable.
    /// </summary>
    event EventHandler<ScreenShareState>? StateChanged;

    /// <summary>Most recent observed state. <see cref="ScreenShareState.Idle"/> until first poll completes.</summary>
    ScreenShareState CurrentState { get; }

    /// <summary>
    /// The window evidence that put the detector into <see cref="ScreenShareState.Active"/>.
    /// Null when state is <see cref="ScreenShareState.Idle"/>.
    /// </summary>
    ActiveShareEvidence? CurrentEvidence { get; }

    /// <summary>Begin polling. Idempotent: a second call while already started is a no-op.</summary>
    void Start();

    /// <summary>Stop polling. Idempotent.</summary>
    void Stop();
}
