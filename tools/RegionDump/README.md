# RegionDump

Visual smoke-test tool for `WhatsAppRegionDetector`. Captures one live frame
of WhatsApp Desktop, runs the region detector on it, and writes two PNG files
you can open and eyeball.

## When to run

- After a WhatsApp Desktop update (layout may have shifted)
- After changing tuning constants in `WhatsAppRegionDetector`
- When verifying dark-mode vs light-mode detection behaviour
- When a regression is suspected after a merge

## How to run

WhatsApp Desktop must be **open and visible on-screen** (not minimized, not
off-screen).

```
dotnet run --project tools/RegionDump/RegionDump.csproj -- <label>
```

Run from any directory. Output PNGs are written to the **current working
directory** (wherever you ran the command), not next to the executable.

**Recommended labels:** `default`, `narrow`, `wide`, `dark-mode`, `light-mode`

### Example

```
cd C:\screenshots\whatsapp-2026-05-02
dotnet run --project C:\path\to\tools\RegionDump\RegionDump.csproj -- dark-mode
```

## Output files

| File | Contents |
|------|----------|
| `region-dump-<label>-raw.png` | Raw captured frame, no annotation |
| `region-dump-<label>-annotated.png` | Frame with detected region outlines |

## How to interpret the annotated PNG

- **Green rectangle** — detected chat-list region (left panel)
- **Red rectangle** — detected conversation region (right panel)
- **No rectangles** — detection failed; read the console output for the reason
- **Red text band across the top** — detection failed with an explicit message

The rectangles should hug the left and right panels of the WhatsApp window,
with the vertical divider between them. If they look wrong or are absent, the
detector likely needs retuning for the current WhatsApp layout.

## Privacy warning

The output PNGs contain a live screenshot of your WhatsApp window, including
message content, contact names, and profile pictures.

- **Never commit these files.** They are excluded by `.gitignore`.
- **Do not share them** without redacting all sensitive content first.
- Delete them when you are done with the test.
