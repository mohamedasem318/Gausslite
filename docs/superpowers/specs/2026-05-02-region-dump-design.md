# RegionDump — Design Spec

**Date:** 2026-05-02
**Branch:** feat/region-detector-smoke-tool
**Status:** Approved

---

## Purpose

`RegionDump` is a standalone Windows diagnostic tool that captures one frame of the live WhatsApp Desktop window, runs `WhatsAppRegionDetector.Detect()` on it, and writes two PNG files:

- `region-dump-<label>-raw.png` — the raw captured frame
- `region-dump-<label>-annotated.png` — the frame with detected region rectangles drawn on it

It is permanently resident in `tools/` and is re-run manually whenever:
- WhatsApp Desktop updates and the layout may have shifted
- Detector tuning constants change
- Dark/light theme behavior needs verification
- Regressions are suspected post-merge

---

## Project layout

```
tools/
  RegionDump/
    RegionDump.csproj
    Program.cs
    README.md
```

Not added to `Gausslite.sln`. Does not modify anything under `src/` or `tests/`.

---

## `RegionDump.csproj`

Mirrors `tools/UiaDump/UiaDump.csproj` exactly, with one addition:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows10.0.22621.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <UseWPF>true</UseWPF>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <WindowsPackageType>None</WindowsPackageType>
    <EnableCoreMrtTooling>false</EnableCoreMrtTooling>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Gausslite.Core\Gausslite.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="System.Drawing.Common" Version="8.0.0" />
  </ItemGroup>
</Project>
```

- `<UseWPF>true</UseWPF>` — required because `RegionDetectionResult` uses `System.Windows.Rect` (from `WindowsBase.dll`)
- `<WindowsPackageType>None</WindowsPackageType>` + `<EnableCoreMrtTooling>false</EnableCoreMrtTooling>` — required by Core's Win2D dependency in an unpackaged app
- `System.Drawing.Common` — Windows-only annotation rendering; no `#if` guards

---

## `Program.cs` — structure

Single file, top-level statements, ~220 lines. Private COM interface types at the bottom.

### Entry point flow

```
args[0] → label (default "unlabeled")
FindWhatsAppWindow()          → HWND or exit(1)
CreateCaptureItem(hwnd)       → GraphicsCaptureItem or exit(1)
CreateD3DDevice()             → IDirect3DDevice
CaptureOneFrame(item, device) → (byte[] pixels, int width, int height, int stride) or exit(1)
SaveRawPng(...)               → region-dump-<label>-raw.png
RunDetector(pixels, ...)      → RegionDetectionResult
SaveAnnotatedPng(...)         → region-dump-<label>-annotated.png
PrintSummary(...)
exit(0)
```

### `FindWhatsAppWindow()`

```csharp
var win32 = new Win32Api();
var profile = new WhatsAppProfile(win32);
var hwnd = profile.FindWindowHandle();
if (hwnd == IntPtr.Zero) { Console.Error.WriteLine("WhatsApp Desktop window not found."); return 1; }
```

### `CreateCaptureItem(IntPtr hwnd)`

Inline `IGraphicsCaptureItemInterop` COM interop — same GUID/pattern as `CaptureItemFactory.cs` in the App project:

1. `GraphicsCaptureSession.IsSupported()` check
2. `WindowsCreateString("Windows.Graphics.Capture.GraphicsCaptureItem", ...)` → HSTRING
3. `RoGetActivationFactory(hstring, IID_IGraphicsCaptureItemInterop, out factoryPtr)`
4. `interop.CreateForWindow(hwnd, IID_IGraphicsCaptureItem, out itemAbi)`
5. `MarshalInterface<GraphicsCaptureItem>.FromAbi(itemAbi)`
6. Cleanup HSTRING and COM pointers in `finally`

Returns `null` on failure; entry point prints error and exits.

### `CreateD3DDevice()`

1. `D3D11CreateDevice(...)` P/Invoke → `IntPtr d3dDevicePtr`
2. `Marshal.QueryInterface(d3dDevicePtr, IID_IDXGIDevice, out dxgiDevicePtr)`
3. `new WinRTCaptureInterop().CreateDirect3DDevice(dxgiDevicePtr)` → `IDirect3DDevice`
4. Release COM pointers; return device

### `CaptureOneFrame(GraphicsCaptureItem item, IDirect3DDevice device)`

`frame.Frame.Surface` is only valid while the `FrameArrived` handler executes — `CaptureEngine` disposes the frame immediately on handler return. Therefore the GPU→CPU copy must happen inside the handler.

```csharp
var engine = new CaptureEngine(new WinRTCaptureInterop(), device);
var gate = new ManualResetEventSlim(false);
(byte[] pixels, int width, int height, int stride)? result = null;

engine.FrameArrived += (_, frame) =>
{
    if (result != null) return;            // only first frame

    // GPU→CPU copy while surface is still valid (before handler returns)
    var softBitmap = SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Frame.Surface)
        .AsTask().GetAwaiter().GetResult();

    using var buffer = softBitmap.LockBuffer(BitmapBufferAccessMode.Read);
    var desc = buffer.GetPlaneDescription(0);
    using var reference = buffer.CreateReference();
    unsafe
    {
        var access = (IMemoryBufferByteAccess)reference;
        access.GetBuffer(out byte* ptr, out uint capacity);
        var pixels = new byte[capacity];
        Marshal.Copy((IntPtr)ptr, pixels, 0, (int)capacity);
        result = (pixels, desc.Width, desc.Height, desc.Stride);
    }
    gate.Set();
    // CaptureEngine disposes frame after this returns — surface is no longer valid
};

engine.Start(item);
bool arrived = gate.Wait(TimeSpan.FromSeconds(2));
engine.Stop();                             // always dispose session

if (!arrived)
{
    Console.Error.WriteLine(
        "Capture timed out after 2s — WhatsApp window may be minimized or off-screen");
    return null;                           // entry point exits non-zero
}
return result!.Value;
```

**Timeout contract:** `engine.Stop()` is called before the null return — the capture session is always disposed cleanly before exit.

### `SaveRawPng(byte[] pixels, int width, int height, int stride, string path)`

```csharp
using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
var bmpData = bmp.LockBits(new Rectangle(0, 0, width, height),
    ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
for (int y = 0; y < height; y++)
    Marshal.Copy(pixels, y * stride, IntPtr.Add(bmpData.Scan0, y * bmpData.Stride), width * 4);
bmp.UnlockBits(bmpData);
bmp.Save(path, ImageFormat.Png);
```

### `SaveAnnotatedPng(byte[] pixels, int width, int height, int stride, RegionDetectionResult result, string path)`

1. Create bitmap from pixel data (same as raw)
2. `Graphics g = Graphics.FromImage(bitmap)`; set `g.CompositingMode = SourceOver`

**Success path** (two rects):
- Chat-list rect: `g.DrawRectangle(new Pen(Color.Green, 3), chatRect)` + label "CHAT LIST" with semi-transparent black background (`Color.FromArgb(160, 0, 0, 0)`) + white text at top-left corner
- Conversation rect: same in `Color.Red` with label "CONVERSATION"

**Failure path**:
- Semi-transparent black band across the top of the image (full width, ~40px)
- Centered red text: `"DETECTION FAILED: <result.FailureReason>"`

3. Save as PNG

### `PrintSummary(...)`

**Success:**
```
Label:        default
Frame size:   1280x800
Detection:    SUCCEEDED
Divider x:    300
Chat list:    (0, 0, 300, 800)
Conversation: (300, 0, 980, 800)
Output:       region-dump-default-raw.png, region-dump-default-annotated.png
```

**Failure:**
```
Label:        default
Frame size:   1280x800
Detection:    FAILED
Failure reason: <result.FailureReason>
Output:       region-dump-default-raw.png, region-dump-default-annotated.png
```

---

## Inline COM types (private, bottom of `Program.cs`)

```csharp
[ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IGraphicsCaptureItemInterop
{
    int CreateForWindow(IntPtr hwnd, ref Guid iid, out IntPtr ppv);
}

[ComImport, Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
unsafe interface IMemoryBufferByteAccess
{
    void GetBuffer(out byte* buffer, out uint capacity);
}
```

Constants:
```csharp
static readonly Guid IID_IGraphicsCaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
static readonly Guid IID_IGraphicsCaptureItem        = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
static readonly Guid IID_IDXGIDevice                 = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
```

---

## Output location

PNGs written to `Environment.CurrentDirectory` — wherever the user invoked the command, not next to the exe.

---

## Privacy and `.gitignore`

Add to root `.gitignore`:
```
region-dump-*.png
```

The raw and annotated PNGs may contain real WhatsApp message content, contact names, and profile pictures. Do not commit. Do not share without redaction.

---

## `README.md` content

Covers:
- What the tool is for (visual regression / smoke testing the region detector)
- When to re-run (WhatsApp update, detector tuning, theme change, suspected regression)
- How to run: `dotnet run --project tools/RegionDump/RegionDump.csproj -- <label>`
- Recommended labels: `default`, `narrow`, `wide`, `dark-mode`, `light-mode`
- Privacy warning: do not commit output PNGs; they may contain message content
- How to interpret the annotated PNG: green = chat list, red = conversation, no rectangles = detection failed (read console for reason)

---

## Out of scope

- No unit tests for the tool
- No integration with the main app
- No modifications to `src/`, `tests/`, or `Gausslite.sln`
- No changes to `WhatsAppRegionDetector` — if the detector misbehaves, log it in console output only

---

## Verification

1. `dotnet build tools/RegionDump/RegionDump.csproj` — clean build, zero warnings
2. With WhatsApp Desktop running: `dotnet run --project tools/RegionDump/RegionDump.csproj -- default` → both PNGs appear in current directory, console summary printed
3. Eyeball the annotated PNG: green rect on left (chat list), red rect on right (conversation), divider between them
