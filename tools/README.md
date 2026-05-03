# Tools — diagnostic / recon utilities

These are throw-away helpers used during Gausslite development to probe
specific behaviors of WhatsApp Desktop, sharing apps (Zoom / Teams / etc.),
or the underlying Windows subsystems. **They are not part of the shipped
app**, and the installer / portable zip does not include them.

If you're a user, you can ignore this directory entirely.

If you're a developer working on Gausslite, the tools are how we capture
ground truth before writing features. The pattern is roughly: write a small
console probe → user runs it during a real-world scenario → output gets
diffed → that diff drives the next code change. Cheap, focused, throwaway.

## What's in here

| Tool | Purpose | Used in |
|---|---|---|
| [`UiaDump`](UiaDump/README.md) | Dumps WhatsApp Desktop's UI Automation tree to a text log. | v0.2.0 — confirmed WhatsApp is a WebView2 shell whose chat content is invisible to UIA, leading to the CV-only `WhatsAppRegionDetector`. |
| [`RegionDump`](RegionDump/README.md) | Captures one WhatsApp frame, runs `WhatsAppRegionDetector`, writes raw + annotated PNGs. | v0.2.0 — visual smoke tests after any region-detector change. |
| [`ShareProbe`](ShareProbe/README.md) | Enumerates all visible top-level windows. Used to capture share-control window signatures before / during / after an active screen share. | v0.3.0 — built the `KnownShareSignatures` table for Zoom / Teams / browser-based shares. |
| [`DiscordProbe`](DiscordProbe/README.md) | Like ShareProbe but recursively enumerates child windows under each Discord top-level window. | v0.3.0 — confirmed Discord renders share controls as Chromium web content invisible to GDI window enumeration. Result documented as a known limitation ([#38](https://github.com/mohamedasem318/Gausslite/issues/38)). |

## Conventions

- Each tool has its own folder with a `.csproj`, `Program.cs`, and a
  `README.md`.
- Output files (`.txt`, `.log`, `.png`) are gitignored via
  [`.gitignore`](.gitignore) — recon outputs can contain personal info
  (window titles, channel names, meeting names) and shouldn't be committed.
- Tools are not in `Gausslite.sln`. Build them individually with:

  ```powershell
  dotnet build tools/<ToolName>/<ToolName>.csproj --configuration Release
  ```

  Or run with `dotnet run --project tools/<ToolName>/<ToolName>.csproj`.

- A tool that grows beyond throwaway gets either: (a) folded into the main
  product as a real feature, or (b) split into its own repo. We don't try
  to maintain an indefinite tools graveyard here.

## Adding a new tool

1. Create `tools/MyProbe/MyProbe.csproj` (model after `ShareProbe.csproj`
   for standalone tools, or `UiaDump.csproj` if you need a project ref to
   `Gausslite.Core`).
2. Add a `Program.cs` with the recon logic.
3. Add `tools/MyProbe/README.md` describing purpose, when to run, how to
   read the output.
4. Update this file's table.
