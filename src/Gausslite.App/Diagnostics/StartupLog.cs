// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO;

namespace Gausslite.App.Diagnostics;

/// <summary>
/// Append-mode startup trace log. Truncated on each app launch so the file
/// always reflects only the most recent run.
/// </summary>
internal static class StartupLog
{
    private static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "gausslite-startup.log");

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

            using var stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);
            writer.Write(sb.ToString());
            writer.Flush();
        }
        catch { /* never throw from logger */ }
    }
}
