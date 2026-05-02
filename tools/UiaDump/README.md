# UiaDump

Throwaway diagnostic tool that dumps the UI Automation tree of the WhatsApp Desktop
window to a plain-text log file. Used during Gausslite v0.2.0 development to discover
what UIA exposes (AutomationIds, ControlTypes, BoundingRects) before writing the
`RegionDetector`.

**Not part of the main solution. Do not commit log files.**

## Build

```
dotnet build tools/UiaDump/UiaDump.csproj -c Release -p:Platform=x64
```

Output lands in `tools/UiaDump/bin/x64/Release/net8.0-windows10.0.22621.0/`.

## Run

Open WhatsApp Desktop, arrange it in the layout you want to capture, then:

```
# From repo root
dotnet run --project tools/UiaDump/UiaDump.csproj -- <label>

# Or run the compiled binary directly
tools\UiaDump\bin\x64\Release\net8.0-windows10.0.22621.0\UiaDump.exe <label>
```

The log is written next to the executable as `uia-dump-<label>.log`.

## Recommended layout labels

Capture each layout with WhatsApp in a representative state:

| Label | WhatsApp state |
|---|---|
| `default-empty` | Default window size, no chat open |
| `default-chat` | Default window size, a chat open with messages visible |
| `narrow-empty` | Window narrowed (~800 px wide), no chat open |
| `narrow-chat` | Window narrowed (~800 px wide), a chat open |
| `wide-empty` | Window maximised / ultra-wide, no chat open |
| `wide-chat` | Window maximised / ultra-wide, a chat open |

## Output format

Plain text, one element per line, indented by depth × 2 spaces:

```
(Pane) Name='WhatsApp' AutomationId='' ClassName='WinUIDesktopWin32WindowClass' LocalizedControlType='pane' Bounds=(100,100,1280,800) Offscreen=False
  (Pane) Name='' AutomationId='MainGrid' ClassName='' LocalizedControlType='pane' Bounds=(108,140,1264,752) Offscreen=False
    (List) Name='Chats' AutomationId='ChatListView' ClassName='' LocalizedControlType='list' Bounds=(108,180,400,712) Offscreen=False
      (ListItem) Name='Mom' AutomationId='' ClassName='' LocalizedControlType='list item' Bounds=(108,180,400,72) Offscreen=False
```

Names longer than 80 characters are truncated with `…`. Names longer than 40 characters
that appear inside a `ListItem` subtree are replaced with `[REDACTED:N chars]` to guard
against message content landing in the log.

The first line of every log file is a privacy warning. **Do not commit or share these files.**
