// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Gausslite.Core.WindowTracking;

/// <summary>
/// Snapshot of a top-level window's identity. Returned by
/// <see cref="IWin32Api.EnumerateVisibleWindows"/>.
/// </summary>
public readonly record struct WindowInfo(
    IntPtr Hwnd,
    uint ProcessId,
    string ProcessName,
    string ClassName,
    string Title);
