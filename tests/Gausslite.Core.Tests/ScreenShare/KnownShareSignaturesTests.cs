using Gausslite.Core.ScreenShare;
using Gausslite.Core.WindowTracking;

namespace Gausslite.Core.Tests.ScreenShare;

/// <summary>
/// Per-app signature regression tests. Each test pins down both a positive case
/// (a window from a real recon session) and a negative case (a non-share window
/// from the same app) so accidental signature drift surfaces here.
/// </summary>
public sealed class KnownShareSignaturesTests
{
    private static WindowInfo Window(string proc, string cls, string title) =>
        new(IntPtr.Zero, 1u, proc, cls, title);

    // ── Zoom ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Zoom_Matches_FloatingShareToolbar()
    {
        var w = Window("Zoom", "ZPFloatToolbarClass", "Screen sharing meeting controls");
        Assert.True(KnownShareSignatures.Zoom.Matches(w));
    }

    [Fact]
    public void Zoom_DoesNotMatch_MainAppWindow()
    {
        // From real recon: ConfMultiTabContentWndClass / "Zoom Workplace" appears whenever Zoom is open.
        var w = Window("Zoom", "ConfMultiTabContentWndClass", "Zoom Workplace");
        Assert.False(KnownShareSignatures.Zoom.Matches(w));
    }

    [Fact]
    public void Zoom_DoesNotMatch_MeetingButNotSharing()
    {
        // From real recon: ConfMultiTabContentWndClass / "Zoom Meeting" appears when in any meeting (audio-only too).
        var w = Window("Zoom", "ConfMultiTabContentWndClass", "Zoom Meeting");
        Assert.False(KnownShareSignatures.Zoom.Matches(w));
    }

    [Fact]
    public void Zoom_DoesNotMatch_OtherFloatToolbar()
    {
        // ZPFloatToolbarClass exists for non-share controls too — title pattern is the discriminator.
        var w = Window("Zoom", "ZPFloatToolbarClass", "Some other Zoom toolbar");
        Assert.False(KnownShareSignatures.Zoom.Matches(w));
    }

    [Fact]
    public void Zoom_ProcessName_IsCaseInsensitive()
    {
        var w = Window("zoom", "ZPFloatToolbarClass", "Screen sharing meeting controls");
        Assert.True(KnownShareSignatures.Zoom.Matches(w));
    }

    // ── Microsoft Teams ──────────────────────────────────────────────────────

    [Fact]
    public void Teams_Matches_SharingControlBar()
    {
        // From real recon: TeamsWebView class, title contains "Sharing control bar".
        var w = Window("ms-teams", "TeamsWebView", "Sharing control bar | Microsoft Teams | Pinned window");
        Assert.True(KnownShareSignatures.Teams.Matches(w));
    }

    [Fact]
    public void Teams_DoesNotMatch_MainAppWindow()
    {
        var w = Window("ms-teams", "TeamsWebView", "Microsoft Teams");
        Assert.False(KnownShareSignatures.Teams.Matches(w));
    }

    [Fact]
    public void Teams_DoesNotMatch_InMeetingButNotSharing()
    {
        // From real recon: in-meeting window has title pattern "Meeting with X | Microsoft Teams"
        var w = Window("ms-teams", "TeamsWebView", "Meeting with Mohamed Asem Adel Mohamed | Microsoft Teams");
        Assert.False(KnownShareSignatures.Teams.Matches(w));
    }

    [Fact]
    public void Teams_DoesNotMatch_NonTeamsClass()
    {
        // Even with a matching title fragment, a non-TeamsWebView class shouldn't trigger.
        var w = Window("ms-teams", "RtcPalVideoPnPMonitorHWND", "Sharing control bar");
        Assert.False(KnownShareSignatures.Teams.Matches(w));
    }

    [Fact]
    public void Teams_ProcessName_IsCaseInsensitive()
    {
        var w = Window("MS-TEAMS", "TeamsWebView", "Sharing control bar | Microsoft Teams | Pinned window");
        Assert.True(KnownShareSignatures.Teams.Matches(w));
    }

    // ── Browser (Chrome / Edge / Chromium) — covers Meet + browser-{Zoom,Teams,Discord} ──

    [Fact]
    public void Browser_Matches_ChromeMeetSharing()
    {
        // From real recon: Chrome spawns Chrome_WidgetWin_1 with title "<domain> is sharing your screen."
        var w = Window("chrome", "Chrome_WidgetWin_1", "meet.google.com is sharing your screen.");
        Assert.True(KnownShareSignatures.Browser.Matches(w));
    }

    [Fact]
    public void Browser_Matches_EdgeBrowserSharing()
    {
        // Edge is Chromium too — same window class, just a different process name.
        var w = Window("msedge", "Chrome_WidgetWin_1", "teams.microsoft.com is sharing your screen.");
        Assert.True(KnownShareSignatures.Browser.Matches(w));
    }

    [Fact]
    public void Browser_DoesNotMatch_NormalChromeWindow()
    {
        // From real recon: regular Chrome browser windows use the same class but different titles.
        var w = Window("chrome", "Chrome_WidgetWin_1", "Meet - oxh-fqxa-fgs - Google Chrome");
        Assert.False(KnownShareSignatures.Browser.Matches(w));
    }

    [Fact]
    public void Browser_DoesNotMatch_NonChromiumProcess()
    {
        // The window class IS shared by Discord (also Chromium), but Browser signature
        // targets actual browsers — process predicate filters Discord out.
        var w = Window("Discord", "Chrome_WidgetWin_1", "is sharing your screen");
        Assert.False(KnownShareSignatures.Browser.Matches(w));
    }

    [Fact]
    public void Browser_ProcessName_IsCaseInsensitive()
    {
        var w = Window("CHROME", "Chrome_WidgetWin_1", "meet.google.com is sharing your screen.");
        Assert.True(KnownShareSignatures.Browser.Matches(w));
    }

    // ── Active set sanity check ──────────────────────────────────────────────

    [Fact]
    public void All_ContainsZoomTeamsAndBrowser()
    {
        Assert.Contains(KnownShareSignatures.Zoom, KnownShareSignatures.All);
        Assert.Contains(KnownShareSignatures.Teams, KnownShareSignatures.All);
        Assert.Contains(KnownShareSignatures.Browser, KnownShareSignatures.All);
    }
}
