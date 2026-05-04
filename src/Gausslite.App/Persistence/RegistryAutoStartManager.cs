// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using Microsoft.Win32;
using Gausslite.App.Diagnostics;

namespace Gausslite.App.Persistence;

/// <summary>
/// Writes the per-user Windows Run key
/// (<c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Gausslite</c>) so
/// the app launches at user login.  Per-user => no admin needed.
///
/// The value is the full path to the current executable, quoted.  When the
/// app is moved (e.g. user reinstalls to a new path), <see cref="IsEnabled"/>
/// will report false because the value no longer matches the current
/// executable; the user can re-enable the toggle to update it.
/// </summary>
internal sealed class RegistryAutoStartManager : IAutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Gausslite";

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            if (key is null) return false;
            string? existing = key.GetValue(ValueName) as string;
            if (string.IsNullOrEmpty(existing)) return false;
            return string.Equals(existing, CurrentExePathQuoted(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            StartupLog.Warn("RegistryAutoStartManager.IsEnabled: registry read failed.", ex);
            return false;
        }
    }

    public bool Enable()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                StartupLog.Warn($"RegistryAutoStartManager.Enable: could not open/create {RunKeyPath}");
                return false;
            }
            key.SetValue(ValueName, CurrentExePathQuoted(), RegistryValueKind.String);
            return true;
        }
        catch (Exception ex)
        {
            StartupLog.Warn("RegistryAutoStartManager.Enable: registry write failed.", ex);
            return false;
        }
    }

    public bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null) return true; // nothing to disable
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex)
        {
            StartupLog.Warn("RegistryAutoStartManager.Disable: registry delete failed.", ex);
            return false;
        }
    }

    private static string CurrentExePathQuoted()
    {
        // Process.MainModule!.FileName resolves to the host .exe (the WPF app),
        // not to dotnet.exe in production builds — apphost mechanism.
        string? path = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(path)) path = Environment.ProcessPath ?? "";
        return $"\"{path}\"";
    }
}
