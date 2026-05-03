// SPDX-License-Identifier: AGPL-3.0-or-later
using NSubstitute;
using Gausslite.Core.AppProfiles;
using Gausslite.Core.WindowTracking;

namespace Gausslite.Core.Tests.AppProfiles;

public sealed class WhatsAppProfileTests
{
    private readonly WhatsAppProfile _profile = new(Substitute.For<IWin32Api>());

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
    public void IsAppWindow_ReturnsExpected(string processName, string className, string title, bool expected) =>
        Assert.Equal(expected, _profile.IsAppWindow(processName, className, title));

    [Fact]
    public void Name_ReturnsWhatsApp() =>
        Assert.Equal("WhatsApp", _profile.Name);

    [Fact]
    public void FindWindowHandle_DelegatesToWin32()
    {
        var win32 = Substitute.For<IWin32Api>();
        var expected = new IntPtr(42);
        win32.FindWindowHandle(Arg.Any<Func<string, string, string, bool>>()).Returns(expected);
        var profile = new WhatsAppProfile(win32);

        var result = profile.FindWindowHandle();

        Assert.Equal(expected, result);
    }
}
