# DiscordProbe

Discord-specific recon tool for v0.3.0 screen-share detection.
[`ShareProbe`](../ShareProbe/) only enumerates top-level windows; this one
walks **every child window** under each Discord top-level window
(recursively, depth-12 cap), so we can see if Discord exposes its share
controls as a child window we can detect.

**Not part of the main solution. Recon `.txt` outputs are gitignored —
they contain Discord channel names and other personal info.**

## Why this exists

Discord is an Electron app. Initial recon with [`ShareProbe`](../ShareProbe/)
showed that Discord's top-level windows are identical whether or not a
screen share is active. Hypothesis: Discord renders share controls as a
child window inside the main render widget host, so they only show up if
we walk children too. This tool tested that hypothesis.

**Result: no.** Even with recursive child enumeration to depth 12, the
window set is identical between sharing and not-sharing. Discord renders
its share UI entirely as Chromium web content, invisible to *any* GDI /
Win32 window enumeration. Verified during v0.3.0 work.

The Discord desktop limitation is documented in
[#38](https://github.com/mohamedasem318/Gausslite/issues/38). Future
detection will require UIA tree-walking (Chromium exposes its accessibility
tree via UIA), which is a different recon path entirely — likely a future
`UiaShareProbe` tool.

## Build

```powershell
dotnet build tools/DiscordProbe/DiscordProbe.csproj --configuration Release
```

## Run

```powershell
# Open Discord, NOT sharing yet:
dotnet run --project tools/DiscordProbe/DiscordProbe.csproj --configuration Release > before-discord-children.txt

# Start a screen share in Discord:
dotnet run --project tools/DiscordProbe/DiscordProbe.csproj --configuration Release > during-discord-children.txt

# Stop sharing.
```

Diff `during` vs `before`. As of v0.3.0, the diff is empty — confirming
the limitation.

## Output format

Tab-separated columns:

```
depth  hwnd  parentHwnd  pid  processName  isVisible  className  title  styleHex  exStyleHex
```

Each Discord top-level window starts a new tree (depth 0); children are
recursively enumerated up to depth 12.

## Process filter

The probe filters by process name `Discord`. To probe a different Electron
app, edit `TargetProcessNames` in `Program.cs`.
