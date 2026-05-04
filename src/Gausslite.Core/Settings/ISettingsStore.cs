// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Gausslite.Core.Settings;

/// <summary>
/// Persistence seam for <see cref="Settings"/>. Implementations live in the
/// hosting assembly because settings location is OS-specific (e.g. Windows
/// LOCALAPPDATA); the interface stays in Core so consumers don't take an OS
/// dependency.
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// Returns the persisted settings, or a new <see cref="Settings"/> with
    /// default values if the backing store is missing, unreadable, or corrupt.
    /// Never throws — settings load failures degrade to first-run behavior.
    /// </summary>
    Settings Load();

    /// <summary>
    /// Persists <paramref name="settings"/>. Returns true on success, false if
    /// the write failed (logged by the implementation; never throws).
    /// </summary>
    bool Save(Settings settings);
}
