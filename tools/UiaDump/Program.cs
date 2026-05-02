using System.IO;
using System.Text;
using System.Windows.Automation;
using Gausslite.Core.AppProfiles;
using Gausslite.Core.WindowTracking;

namespace UiaDump;

internal static class Program
{
    // Safety caps for runaway trees (WhatsApp is well-behaved but UIA can loop on buggy apps)
    private const int MaxElements = 5000;
    private const int MaxDepth = 40;

    [STAThread]
    private static int Main(string[] args)
    {
        string label = args.Length > 0 ? args[0] : "unlabeled";
        string logFile = $"uia-dump-{label}.log";
        string logPath = Path.Combine(AppContext.BaseDirectory, logFile);

        IntPtr hwnd = new WhatsAppProfile(new Win32Api()).FindWindowHandle();

        if (hwnd == IntPtr.Zero)
        {
            Console.Error.WriteLine("ERROR: WhatsApp Desktop window not found. Is WhatsApp running?");
            return 1;
        }

        AutomationElement root;
        try
        {
            root = AutomationElement.FromHandle(hwnd);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: Cannot attach to WhatsApp via UIA: {ex.Message}");
            return 1;
        }

        using var writer = new StreamWriter(logPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        writer.WriteLine("WARNING: This file may contain truncated message content. Do not commit or share.");
        writer.WriteLine($"# Label:    {label}");
        writer.WriteLine($"# Captured: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        writer.WriteLine($"# HWND:     0x{hwnd:X}");
        writer.WriteLine();

        int count = 0;
        bool truncated = WalkTree(root, writer, 0, insideListItem: false, ref count);

        writer.Flush();

        if (truncated)
            Console.Error.WriteLine($"WARNING: Tree truncated at {MaxElements} elements; increase MaxElements if needed.");
        Console.WriteLine($"Dumped {count} elements to {logFile}");
        return 0;
    }

    // Returns true when the element cap was hit (signals caller to stop descending).
    private static bool WalkTree(
        AutomationElement element,
        StreamWriter writer,
        int depth,
        bool insideListItem,
        ref int count)
    {
        if (count >= MaxElements) return true;
        if (depth > MaxDepth) return false;

        count++;

        AutomationElement.AutomationElementInformation info;
        try { info = element.Current; }
        catch (ElementNotAvailableException) { return false; }

        bool nowInsideListItem = insideListItem || info.ControlType == ControlType.ListItem;

        // Redact first (preserving original length in the tag), then truncate display to 80 chars.
        string rawName = info.Name ?? "";
        string name = (nowInsideListItem && rawName.Length > 40)
            ? $"[REDACTED:{rawName.Length} chars]"
            : (rawName.Length > 80 ? rawName[..80] + "…" : rawName);

        string controlType = info.ControlType.ProgrammaticName.Replace("ControlType.", "");
        string automationId = info.AutomationId ?? "";
        string className = info.ClassName ?? "";
        string localizedCT = info.LocalizedControlType ?? "";
        bool offscreen = info.IsOffscreen;

        var b = info.BoundingRectangle;
        string boundsStr = (b.IsEmpty || double.IsInfinity(b.X) || double.IsNaN(b.X))
            ? "(none)"
            : $"({(int)b.Left},{(int)b.Top},{(int)b.Width},{(int)b.Height})";

        writer.WriteLine(
            $"{new string(' ', depth * 2)}" +
            $"({controlType}) " +
            $"Name='{name}' " +
            $"AutomationId='{automationId}' " +
            $"ClassName='{className}' " +
            $"LocalizedControlType='{localizedCT}' " +
            $"Bounds={boundsStr} " +
            $"Offscreen={offscreen}");

        AutomationElement? child;
        try { child = TreeWalker.RawViewWalker.GetFirstChild(element); }
        catch { return count >= MaxElements; }

        while (child != null)
        {
            if (WalkTree(child, writer, depth + 1, nowInsideListItem, ref count))
                return true;

            try { child = TreeWalker.RawViewWalker.GetNextSibling(child); }
            catch { break; }
        }

        return false;
    }
}
