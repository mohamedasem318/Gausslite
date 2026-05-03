// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO;
using System.Text;

namespace Gausslite.Core.Diagnostics;

/// <summary>
/// Append-mode diagnostic trace log shared by Gausslite.Core and Gausslite.Overlay.
/// Writes to the same gausslite-startup.log file as Gausslite.App's StartupLog;
/// StartupLog truncates the file on each app launch so both writers see a clean slate.
/// </summary>
internal static class DiagLog
{
    // Mirrors Gausslite.App.Diagnostics.StartupLog.MaxLogBytes. Both classes write to the
    // same file; each enforces the cap independently via a stat() check before appending.
    // Cross-class race windows can't grow the file past ~2× the cap, which is acceptable.
    private const long MaxLogBytes = 5 * 1024 * 1024; // 5 MB

    private static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "gausslite-startup.log");

    private static readonly object _writeLock = new();

    internal static void Info(string message) => Write("INFO", message, null);

    internal static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append($"[{DateTime.Now:yyyy-MM-ddTHH:mm:ss.fff}] [{level}] {message}");
            if (ex is not null)
            {
                for (var e = ex; e is not null; e = e.InnerException)
                    sb.Append($"{Environment.NewLine}  {e.GetType().FullName}: {e.Message}{Environment.NewLine}{e.StackTrace}");
            }
            sb.AppendLine();
            string entry = sb.ToString();

            lock (_writeLock)
            {
                try
                {
                    var fi = new FileInfo(LogPath);
                    if (fi.Exists && fi.Length > MaxLogBytes)
                        File.WriteAllText(LogPath, $"[{DateTime.Now:yyyy-MM-ddTHH:mm:ss.fff}] [INFO] gausslite-startup.log truncated (exceeded {MaxLogBytes / (1024 * 1024)} MB cap){Environment.NewLine}");
                }
                catch { /* size check is best-effort; fall through and keep writing */ }

                using var stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream);
                writer.Write(entry);
                writer.Flush();
            }
        }
        catch { /* never throw from logger */ }
    }
}
