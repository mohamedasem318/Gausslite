// SPDX-License-Identifier: AGPL-3.0-or-later
using Gausslite.Core.Blur;

namespace Gausslite.Core.Settings;

/// <summary>
/// User-configurable settings persisted across launches.  Immutable record;
/// updates produce a new instance via <c>with</c>-expressions and are saved
/// through <see cref="ISettingsStore.Save"/>.
///
/// All properties have sensible defaults so a missing or corrupt settings file
/// degrades gracefully to first-run behavior.
/// </summary>
public sealed record Settings
{
    /// <summary>
    /// Currently selected blur intensity preset. Default: Medium.
    /// </summary>
    public BlurIntensityPreset Intensity { get; init; } = BlurIntensityPreset.Medium;

    /// <summary>
    /// Currently selected blur region scope. Default: Both (full overlay).
    /// </summary>
    public BlurRegionScope Scope { get; init; } = BlurRegionScope.Both;

    /// <summary>
    /// True when the app is registered to start with Windows
    /// (HKCU\…\Run\Gausslite). Default: off.
    /// The actual registry state is managed by <c>IAutoStartManager</c>; this
    /// field reflects the user's intent and is reconciled with the registry
    /// at startup and on toggle.
    /// </summary>
    public bool AutoStart { get; init; } = false;

    /// <summary>
    /// Opt-in heuristic: when true, blur activates whenever any known sharing
    /// app's process is running (Zoom desktop / Teams desktop / Discord
    /// desktop), regardless of whether an active share-control window is
    /// present.  Workaround for apps whose share-control UI is invisible to
    /// window enumeration (notably Discord desktop — see issue #38).  Default
    /// off because for most users it would mean blur is on whenever Zoom
    /// lives in the system tray.
    /// </summary>
    public bool ProcessRunningHeuristicEnabled { get; init; } = false;
}
