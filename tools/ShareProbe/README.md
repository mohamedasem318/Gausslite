# ShareProbe

Diagnostic tool that enumerates every visible top-level window on the desktop
and dumps a tab-separated row per window. Used during v0.3.0 development to
capture the window signatures of share-control toolbars from real share
sessions in Zoom / Teams / browser-based meetings.

**Not part of the main solution. Recon `.txt` outputs are gitignored —
they contain meeting titles, window names, and other personal info.**

## Build

```powershell
dotnet build tools/ShareProbe/ShareProbe.csproj --configuration Release
```

## Run

```powershell
# From repo root
dotnet run --project tools/ShareProbe/ShareProbe.csproj --configuration Release > before.txt
```

Or run the compiled executable directly from
`tools/ShareProbe/bin/Release/net8.0-windows/`.

## Recon protocol — capturing a share signature

For each app you want to characterize (Zoom desktop, Teams desktop,
Chrome / Edge for browser-based shares), do three runs:

1. **App open, NOT sharing**:
   `dotnet run --configuration Release > before-<app>.txt`
2. **Start a screen share** in the app (any monitor / window).
3. **Capture during share**:
   `dotnet run --configuration Release > during-<app>.txt`
4. **Stop sharing**.

The diff (`during.txt` minus `before.txt`) reveals the share-only windows
the app spawned during an active share. Look for entries whose `className`
or `title` contains words like `share`, `sharing`, `screen`, `stop`,
`control`, etc. That's your signature for `KnownShareSignatures.cs`.

## Output format

Tab-separated columns:

```
hwnd  pid  processName  isVisible  isIconic  className  title  ownerHwnd  exStyleHex
```

Sorted by `processName`, then `className`, then `title` for reproducible
diffs.

## Real outputs from the v0.3.0 recon

| App | Class | Title pattern (during share) |
|---|---|---|
| Zoom desktop | `ZPFloatToolbarClass` | `Screen sharing meeting controls` |
| Microsoft Teams desktop | `TeamsWebView` | `Sharing control bar | Microsoft Teams | Pinned window` |
| Chrome (any browser-based share) | `Chrome_WidgetWin_1` | `<domain> is sharing your screen.` (e.g. `meet.google.com is sharing your screen.`) |
| Edge | `Chrome_WidgetWin_1` | same as Chrome (also Chromium) |
| Discord desktop | *(no signature found)* | Discord's share UI is rendered as Chromium web content; not visible to top-level enumeration. See [`DiscordProbe`](../DiscordProbe/) for the deeper recon. |

These signatures live in
[`src/Gausslite.Core/ScreenShare/KnownShareSignatures.cs`](../../src/Gausslite.Core/ScreenShare/KnownShareSignatures.cs).
