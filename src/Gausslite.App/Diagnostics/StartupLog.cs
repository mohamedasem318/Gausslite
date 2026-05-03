// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO;

namespace Gausslite.App.Diagnostics;

/// <summary>
/// Append-mode startup trace log. Truncated on each app launch so the file
/// always reflects only the most recent run; also auto-truncated mid-session
/// if it grows past <see cref="MaxLogBytes"/> to bound disk usage on
/// long-running tray sessions.
/// </summary>
internal static class StartupLog
{
    /// <summary>Maximum size of gausslite-startup.log; auto-truncated when exceeded.</summary>
    internal const long MaxLogBytes = 5 * 1024 * 1024; // 5 MB

    private static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "gausslite-startup.log");

    private static readonly object _writeLock = new();

    static StartupLog()
    {
        try { File.WriteAllText(LogPath, string.Empty); }
        catch { /* never throw from logger init */ }
    }

    internal static void Info(string message) => Write("INFO", message, null);

    internal static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"[{DateTime.Now:yyyy-MM-ddTHH:mm:ss.fff}] [{level}] {message}");
            if (ex is not null)
            {
                for (var e = ex; e is not null; e = e.InnerException)
                    sb.Append($"{Environment.NewLine}  {e.GetType().FullName}: {e.Message}{Environment.NewLine}{e.StackTrace}");
            }
            sb.AppendLine();
            string entry = sb.ToString();

            // Lock so a parallel writer (Core's DiagLog targets the same file) can't
            // race the size-check / truncate / append sequence.
            lock (_writeLock)
            {
                // Bound disk usage on long-running tray sessions: truncate before append if
                // the existing file size would push past MaxLogBytes.  FileInfo.Length is a
                // cheap stat call; production write rate is low (transition-only), so the
                // overhead is invisible.
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
