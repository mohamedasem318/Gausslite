using System.Diagnostics;
using Gausslite.App.Orchestration;
using Gausslite.Core.WindowTracking;

namespace Gausslite.App.Tests.Orchestration;

/// <summary>
/// Tests for <see cref="CaptureItemFactory"/>. Integration-style tests use the real Win32 API
/// and do not require WhatsApp to be installed. Predicate tests are pure and do not enumerate
/// real windows.
/// </summary>
public sealed class CaptureItemFactoryTests
{
    // ── Integration tests (real Win32 API, no WhatsApp required) ─────────────

    /// <summary>
    /// When WhatsApp is not running, TryCreateForWhatsApp must return false with a null item
    /// and must not throw. Skipped on machines where WhatsApp is already running (detection success
    /// would correctly return true there — validated by the predicate tests instead).
    /// </summary>
    [Fact]
    public void TryCreateForWhatsApp_WhenWhatsAppNotRunning_ReturnsFalseAndNull()
    {
        bool whatsAppRunning = Process.GetProcesses()
            .Any(p => p.ProcessName.StartsWith("WhatsApp", StringComparison.OrdinalIgnoreCase));
        if (whatsAppRunning) return; // precondition not met on this machine; skip

        var factory = new CaptureItemFactory(new Win32Api());

        bool result = factory.TryCreateForWhatsApp(out var item);

        Assert.False(result);
        Assert.Null(item);
    }

    /// <summary>
    /// Calling TryCreateForWhatsApp multiple times must never throw, even after a false return.
    /// </summary>
    [Fact]
    public void TryCreateForWhatsApp_CalledTwice_NeverThrows()
    {
        var factory = new CaptureItemFactory(new Win32Api());

        var ex = Record.Exception(() =>
        {
            factory.TryCreateForWhatsApp(out _);
            factory.TryCreateForWhatsApp(out _);
        });

        Assert.Null(ex);
    }

    // ── IsWhatsAppWindow predicate unit tests (pure, no real window enumeration) ─

    // Matches by process name prefix "WhatsApp"
    [Theory]
    [InlineData("WhatsApp.Root", "WinUIDesktopWin32WindowClass", "WhatsApp", true)]
    [InlineData("WhatsApp", "Chrome_WidgetWin_1", "WhatsApp", true)]
    [InlineData("whatsapp", "anything", "WhatsApp", true)]
    [InlineData("WHATSAPP", "anything", "WhatsApp", true)]
    [InlineData("WhatsAppDesktop", "Chrome_WidgetWin_1", "WhatsApp", true)]
    [InlineData("WhatsApp.exe", "anything", "My Chat", true)]
    // Matches by WinUI3 class + title (belt-and-suspenders for future Store builds)
    [InlineData("SomeProcess", "WinUIDesktopWin32WindowClass", "WhatsApp", true)]
    [InlineData("SomeProcess", "WinUIDesktopWin32WindowClass", "WhatsApp Desktop", true)]
    // Rejects WebView2 child regardless of other fields
    [InlineData("msedgewebview2", "Chrome_WidgetWin_1", "WhatsApp", false)]
    [InlineData("msEdgeWebView2", "WinUIDesktopWin32WindowClass", "WhatsApp", false)]
    // Rejects non-WhatsApp processes
    [InlineData("notepad", "Notepad", "WhatsApp", false)]
    [InlineData("ApplicationFrameHost", "ApplicationFrameWindow", "WhatsApp", false)]
    [InlineData("chrome", "Chrome_WidgetWin_1", "WhatsApp", false)]
    // Rejects empty title (window not ready / minimised to tray)
    [InlineData("WhatsApp.Root", "WinUIDesktopWin32WindowClass", "", false)]
    [InlineData("WhatsApp", "anything", "", false)]
    // WinUI3 class match requires title to contain "WhatsApp"
    [InlineData("SomeProcess", "WinUIDesktopWin32WindowClass", "Microsoft Edge", false)]
    [InlineData("SomeProcess", "WinUIDesktopWin32WindowClass", "", false)]
    // Class name matching is case-sensitive for the WinUI3 branch
    [InlineData("SomeProcess", "winuidesktopwin32windowclass", "WhatsApp", false)]
    public void IsWhatsAppWindow_ReturnsExpected(string processName, string className, string title, bool expected) =>
        Assert.Equal(expected, CaptureItemFactory.IsWhatsAppWindow(processName, className, title));
}
