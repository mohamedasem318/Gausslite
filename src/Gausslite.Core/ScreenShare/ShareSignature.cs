// SPDX-License-Identifier: AGPL-3.0-or-later
using Gausslite.Core.WindowTracking;

namespace Gausslite.Core.ScreenShare;

/// <summary>
/// One per-app rule for detecting an active screen share by matching against
/// visible top-level windows.  A signature matches when ALL three predicates
/// (process / class / title) return true for the same window.
/// </summary>
internal sealed class ShareSignature
{
    public required string AppName { get; init; }

    /// <summary>Match against <see cref="WindowInfo.ProcessName"/> (case-insensitive comparisons recommended).</summary>
    public required Func<string, bool> ProcessNameMatches { get; init; }

    /// <summary>Match against <see cref="WindowInfo.ClassName"/> (case-sensitive — Win32 class names are exact).</summary>
    public required Func<string, bool> ClassNameMatches { get; init; }

    /// <summary>Match against <see cref="WindowInfo.Title"/> (case-insensitive comparisons recommended).</summary>
    public required Func<string, bool> TitleMatches { get; init; }

    /// <summary>True when all three predicates match the given window.</summary>
    public bool Matches(WindowInfo w) =>
        ProcessNameMatches(w.ProcessName) &&
        ClassNameMatches(w.ClassName) &&
        TitleMatches(w.Title);
}
