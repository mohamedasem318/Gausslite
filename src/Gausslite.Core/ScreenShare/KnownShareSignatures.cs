// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Gausslite.Core.ScreenShare;

/// <summary>
/// The well-known share-control window signatures for each supported sharing app.
/// Captured via <c>tools/ShareProbe</c> recon during a real screen share — see
/// PR notes for the exact run output that produced each entry.
///
/// **Add a new app** by appending a <see cref="ShareSignature"/> to <see cref="All"/>.
/// Each signature must match a window that ONLY appears during an active share —
/// not one that's there whenever the app is running or in a meeting.
/// </summary>
internal static class KnownShareSignatures
{
    /// <summary>
    /// Zoom desktop client. Active share spawns a floating control toolbar with
    /// class <c>ZPFloatToolbarClass</c> and title <c>"Screen sharing meeting controls"</c>.
    /// The class is unique to Zoom; the title nails the specific toolbar (vs. other
    /// ZPFloatToolbarClass instances Zoom uses for non-share controls).
    /// </summary>
    public static readonly ShareSignature Zoom = new()
    {
        AppName = "Zoom",
        ProcessNameMatches = p => p.Equals("Zoom", StringComparison.OrdinalIgnoreCase),
        ClassNameMatches = c => c == "ZPFloatToolbarClass",
        TitleMatches = t => t.Contains("Screen sharing", StringComparison.OrdinalIgnoreCase),
    };

    /// <summary>
    /// Microsoft Teams desktop client. Active share spawns a webview-hosted control
    /// bar whose title contains <c>"Sharing control bar"</c>. The class is the
    /// generic <c>TeamsWebView</c> shell, so we anchor on the title.
    /// </summary>
    public static readonly ShareSignature Teams = new()
    {
        AppName = "Microsoft Teams",
        ProcessNameMatches = p => p.Equals("ms-teams", StringComparison.OrdinalIgnoreCase),
        ClassNameMatches = c => c == "TeamsWebView",
        TitleMatches = t => t.Contains("Sharing control bar", StringComparison.OrdinalIgnoreCase),
    };

    /// <summary>
    /// Browser-based screen sharing (Chrome / Edge / any Chromium browser).  When a tab
    /// uses <c>getDisplayMedia()</c>, the browser spawns a generic floating notification
    /// window with class <c>Chrome_WidgetWin_1</c> and title
    /// <c>"&lt;domain&gt; is sharing your screen."</c> (Chrome) or similar on Edge.
    /// One signature catches Google Meet, browser Zoom, browser Teams, browser Discord,
    /// and anything else that uses the same WebRTC display-capture API.
    /// </summary>
    public static readonly ShareSignature Browser = new()
    {
        AppName = "Browser (Meet / Web)",
        ProcessNameMatches = p =>
            p.Equals("chrome", StringComparison.OrdinalIgnoreCase) ||
            p.Equals("msedge", StringComparison.OrdinalIgnoreCase),
        ClassNameMatches = c => c == "Chrome_WidgetWin_1",
        TitleMatches = t => t.Contains("is sharing your screen", StringComparison.OrdinalIgnoreCase),
    };

    // Discord desktop signature pending — see DiscordProbe recon results.

    /// <summary>The active set of signatures the detector polls against.</summary>
    public static readonly IReadOnlyList<ShareSignature> All = new[] { Zoom, Teams, Browser };
}
