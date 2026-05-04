// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Gausslite.App.Persistence;

/// <summary>
/// Reads and writes the OS-level "start with Windows" registration for the
/// app.  On Windows this is a value under
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>; the per-user
/// hive is used so no admin elevation is required.
///
/// Methods never throw — registry IO failures (key removed by some other
/// utility, security-software interference) return <c>false</c> and log via
/// <c>StartupLog</c>.
/// </summary>
public interface IAutoStartManager
{
    /// <summary>
    /// True when the registry entry exists and points at the current
    /// executable.  False when the entry is missing, points elsewhere, or the
    /// read failed.
    /// </summary>
    bool IsEnabled();

    /// <summary>
    /// Writes the registry entry pointing at the current executable.  Returns
    /// true if the write succeeded (or the entry was already correct).
    /// </summary>
    bool Enable();

    /// <summary>
    /// Removes the registry entry.  Returns true if the entry was deleted
    /// or was already absent.
    /// </summary>
    bool Disable();
}
