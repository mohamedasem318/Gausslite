// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Gausslite.Core.ScreenShare;

/// <summary>
/// Diagnostic payload describing which window matched a known share-control signature.
/// Returned alongside <see cref="ScreenShareState.Active"/> so logs can pinpoint which
/// app and which window provoked the state transition.
/// </summary>
public readonly record struct ActiveShareEvidence(
    string AppName,
    string ProcessName,
    string WindowClass,
    string WindowTitle,
    IntPtr Hwnd);
