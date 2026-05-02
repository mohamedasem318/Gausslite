# RegionDump Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a standalone console tool that captures one live WhatsApp Desktop frame, runs `WhatsAppRegionDetector.Detect()` on it, and writes a raw PNG and an annotated PNG to the current working directory for visual smoke testing.

**Architecture:** Single `Program.cs` file using C# top-level statements. Static local functions implement each pipeline stage. COM interop interfaces and a `NativeMethods` helper class sit at the bottom of the same file. References `Gausslite.Core` for window finding (`WhatsAppProfile`/`Win32Api`), WGC frame capture (`CaptureEngine`/`WinRTCaptureInterop`), and detection (`WhatsAppRegionDetector`). GPU→CPU readback uses `Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromSurfaceAsync` (in-box with the Windows TFM, MTA-safe). PNG annotation uses `System.Drawing.Common` (GDI+, Windows-only, no `#if` guards).

**Tech Stack:** .NET 8, `net8.0-windows10.0.22621.0`, x64. Windows Graphics Capture, `Windows.Graphics.Imaging.SoftwareBitmap`, `System.Drawing.Common` 8.0.0, COM P/Invoke (`combase.dll`, `d3d11.dll`).

---

### Task 1: Project scaffold

**Files:**
- Create: `tools/RegionDump/RegionDump.csproj`
- Create: `tools/RegionDump/Program.cs`

- [ ] **Step 1: Create `tools/RegionDump/RegionDump.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows10.0.22621.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <UseWPF>true</UseWPF>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
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

Notes:
- `<UseWPF>true</UseWPF>` — `RegionDetectionResult` uses `System.Windows.Rect` (from `WindowsBase.dll`)
- `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` — needed for `byte*` in `IMemoryBufferByteAccess`
- `<WindowsPackageType>None</WindowsPackageType>` + `<EnableCoreMrtTooling>false</EnableCoreMrtTooling>` — required by Core's Win2D dependency in an unpackaged app

- [ ] **Step 2: Create a minimal `tools/RegionDump/Program.cs`**

```csharp
Console.WriteLine("RegionDump placeholder");
```

- [ ] **Step 3: Verify the build compiles**

```
dotnet build tools/RegionDump/RegionDump.csproj -p:Platform=x64
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```
git add tools/RegionDump/RegionDump.csproj tools/RegionDump/Program.cs
git commit -m "feat(tools/region-dump): scaffold RegionDump project"
```

---

### Task 2: Privacy guard

**Files:**
- Modify: `.gitignore`

- [ ] **Step 1: Append to root `.gitignore`**

Open `.gitignore` and add at the very end:

```
# RegionDump outputs — may contain WhatsApp message content; never commit
region-dump-*.png
```

- [ ] **Step 2: Verify the pattern is recognised**

```
git check-ignore -v region-dump-default-raw.png
```

Expected output contains: `region-dump-*.png    region-dump-default-raw.png`

- [ ] **Step 3: Commit**

```
git add .gitignore
git commit -m "chore: exclude region-dump PNG outputs from version control"
```

---

### Task 3: COM boilerplate — interfaces, P/Invoke, GUIDs

**Files:**
- Modify: `tools/RegionDump/Program.cs`

This task replaces the placeholder with a skeleton that has no logic yet but defines all the COM plumbing the later tasks will use.

- [ ] **Step 1: Replace `Program.cs` entirely with the following**

```csharp
using System.Runtime.InteropServices;
using Gausslite.Core.AppProfiles;
using Gausslite.Core.Capture;
using Gausslite.Core.Detection;
using Gausslite.Core.WindowTracking;
using System.Drawing;
using System.Drawing.Imaging;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using WinRT;

// ── Entry point ──────────────────────────────────────────────────────────────

Console.WriteLine("RegionDump placeholder");

// ── COM interfaces ────────────────────────────────────────────────────────────

[ComImport]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IGraphicsCaptureItemInterop
{
    int CreateForWindow(IntPtr hwnd, ref Guid iid, out IntPtr ppv);
}

[ComImport]
[Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
unsafe interface IMemoryBufferByteAccess
{
    void GetBuffer(out byte* buffer, out uint capacity);
}

// ── P/Invoke + GUIDs ──────────────────────────────────────────────────────────

static class NativeMethods
{
    public static readonly Guid IID_IGraphicsCaptureItemInterop =
        new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    public static readonly Guid IID_IGraphicsCaptureItem =
        new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    public static readonly Guid IID_IDXGIDevice =
        new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    [DllImport("combase.dll", PreserveSig = true)]
    public static extern int RoGetActivationFactory(
        IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    [DllImport("combase.dll", PreserveSig = true)]
    public static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
        uint length,
        out IntPtr hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    public static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("d3d11.dll", PreserveSig = true)]
    public static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        int DriverType,         // D3D_DRIVER_TYPE_HARDWARE = 1
        IntPtr Software,
        uint Flags,
        IntPtr pFeatureLevels,
        uint FeatureLevels,
        uint SDKVersion,        // D3D11_SDK_VERSION = 7
        out IntPtr ppDevice,
        IntPtr pFeatureLevel,
        out IntPtr ppImmediateContext);
}
```

- [ ] **Step 2: Verify the build compiles**

```
dotnet build tools/RegionDump/RegionDump.csproj -p:Platform=x64
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```
git add tools/RegionDump/Program.cs
git commit -m "feat(tools/region-dump): add COM interfaces, P/Invoke declarations, and GUID constants"
```

---

### Task 4: Window finding and capture item creation

**Files:**
- Modify: `tools/RegionDump/Program.cs`

Add two static local functions. Insert them between the `Console.WriteLine("RegionDump placeholder")` line and the `// ── COM interfaces ──` comment.

- [ ] **Step 1: Add `FindWhatsAppWindow` and `CreateCaptureItem` after the entry-point placeholder**

```csharp
static IntPtr FindWhatsAppWindow()
{
    var win32 = new Win32Api();
    var profile = new WhatsAppProfile(win32);
    return profile.FindWindowHandle();
}

static GraphicsCaptureItem? CreateCaptureItem(IntPtr hwnd)
{
    if (!GraphicsCaptureSession.IsSupported())
    {
        Console.Error.WriteLine("Windows.Graphics.Capture is not supported on this system.");
        return null;
    }

    const string runtimeClass = "Windows.Graphics.Capture.GraphicsCaptureItem";
    int hr = NativeMethods.WindowsCreateString(
        runtimeClass, (uint)runtimeClass.Length, out IntPtr hstring);
    if (hr < 0)
    {
        Console.Error.WriteLine($"WindowsCreateString failed: 0x{hr:X8}");
        return null;
    }

    IntPtr factoryPtr = IntPtr.Zero;
    IntPtr itemAbi    = IntPtr.Zero;
    try
    {
        var interopIid = NativeMethods.IID_IGraphicsCaptureItemInterop;
        hr = NativeMethods.RoGetActivationFactory(hstring, ref interopIid, out factoryPtr);
        if (hr < 0)
        {
            Console.Error.WriteLine($"RoGetActivationFactory failed: 0x{hr:X8}");
            return null;
        }

        var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);
        var itemIid  = NativeMethods.IID_IGraphicsCaptureItem;
        hr = interop.CreateForWindow(hwnd, ref itemIid, out itemAbi);
        if (hr < 0)
        {
            Console.Error.WriteLine($"CreateForWindow failed: 0x{hr:X8}");
            return null;
        }

        return MarshalInterface<GraphicsCaptureItem>.FromAbi(itemAbi);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"CreateCaptureItem error: {ex.Message}");
        return null;
    }
    finally
    {
        if (itemAbi    != IntPtr.Zero) Marshal.Release(itemAbi);
        if (factoryPtr != IntPtr.Zero) Marshal.Release(factoryPtr);
        NativeMethods.WindowsDeleteString(hstring);
    }
}
```

- [ ] **Step 2: Verify the build compiles**

```
dotnet build tools/RegionDump/RegionDump.csproj -p:Platform=x64
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```
git add tools/RegionDump/Program.cs
git commit -m "feat(tools/region-dump): add FindWhatsAppWindow and CreateCaptureItem helpers"
```

---

### Task 5: D3D device creation and frame capture with timeout

**Files:**
- Modify: `tools/RegionDump/Program.cs`

Add two more static local functions after `CreateCaptureItem`, still before the `// ── COM interfaces ──` comment.

- [ ] **Step 1: Add `CreateD3DDevice`**

```csharp
static IDirect3DDevice CreateD3DDevice()
{
    int hr = NativeMethods.D3D11CreateDevice(
        IntPtr.Zero,
        1,              // D3D_DRIVER_TYPE_HARDWARE
        IntPtr.Zero,
        0,
        IntPtr.Zero,
        0,
        7,              // D3D11_SDK_VERSION
        out IntPtr d3dDevicePtr,
        IntPtr.Zero,
        out IntPtr _);
    Marshal.ThrowExceptionForHR(hr);

    try
    {
        var dxgiIid = NativeMethods.IID_IDXGIDevice;
        hr = Marshal.QueryInterface(d3dDevicePtr, ref dxgiIid, out IntPtr dxgiDevicePtr);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            return new WinRTCaptureInterop().CreateDirect3DDevice(dxgiDevicePtr);
        }
        finally
        {
            Marshal.Release(dxgiDevicePtr);
        }
    }
    finally
    {
        Marshal.Release(d3dDevicePtr);
    }
}
```

- [ ] **Step 2: Add `CaptureOneFrame`**

```csharp
static (byte[] pixels, int width, int height, int stride)? CaptureOneFrame(
    GraphicsCaptureItem item, IDirect3DDevice device)
{
    var engine = new CaptureEngine(new WinRTCaptureInterop(), device);
    var gate   = new System.Threading.ManualResetEventSlim(false);
    (byte[] pixels, int width, int height, int stride)? result = null;

    engine.FrameArrived += (_, frame) =>
    {
        if (result != null) return;

        // GPU→CPU copy must happen while the surface is still valid.
        // CaptureEngine disposes the frame immediately after this handler returns,
        // so we cannot store frame.Frame.Surface and use it later.
        var softBitmap = SoftwareBitmap
            .CreateCopyFromSurfaceAsync(frame.Frame.Surface)
            .AsTask().GetAwaiter().GetResult();

        using var buffer    = softBitmap.LockBuffer(BitmapBufferAccessMode.Read);
        var desc            = buffer.GetPlaneDescription(0);
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
    };

    engine.Start(item);
    bool arrived = gate.Wait(TimeSpan.FromSeconds(2));
    engine.Stop();   // always dispose the capture session, including on timeout

    if (!arrived)
    {
        Console.Error.WriteLine(
            "Capture timed out after 2s — WhatsApp window may be minimized or off-screen");
        return null;
    }
    return result!.Value;
}
```

- [ ] **Step 3: Verify the build compiles**

```
dotnet build tools/RegionDump/RegionDump.csproj -p:Platform=x64
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```
git add tools/RegionDump/Program.cs
git commit -m "feat(tools/region-dump): add CreateD3DDevice and CaptureOneFrame helpers"
```

---

### Task 6: PNG output helpers

**Files:**
- Modify: `tools/RegionDump/Program.cs`

Add three static local functions after `CaptureOneFrame`, still before the `// ── COM interfaces ──` comment.

- [ ] **Step 1: Add `SaveRawPng`**

```csharp
static void SaveRawPng(byte[] pixels, int width, int height, int stride, string path)
{
    using var bmp     = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    var bmpData       = bmp.LockBits(
        new Rectangle(0, 0, width, height),
        ImageLockMode.WriteOnly,
        PixelFormat.Format32bppArgb);
    for (int y = 0; y < height; y++)
        Marshal.Copy(pixels, y * stride,
            IntPtr.Add(bmpData.Scan0, y * bmpData.Stride), width * 4);
    bmp.UnlockBits(bmpData);
    bmp.Save(path, ImageFormat.Png);
}
```

- [ ] **Step 2: Add `SaveAnnotatedPng`**

```csharp
static void SaveAnnotatedPng(
    byte[] pixels, int width, int height, int stride,
    RegionDetectionResult detection, string path)
{
    using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    var bmpData   = bmp.LockBits(
        new Rectangle(0, 0, width, height),
        ImageLockMode.WriteOnly,
        PixelFormat.Format32bppArgb);
    for (int y = 0; y < height; y++)
        Marshal.Copy(pixels, y * stride,
            IntPtr.Add(bmpData.Scan0, y * bmpData.Stride), width * 4);
    bmp.UnlockBits(bmpData);

    using var g = Graphics.FromImage(bmp);
    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;

    using var labelFont = new Font("Consolas", 12f, FontStyle.Regular, GraphicsUnit.Point);
    using var textBrush = new SolidBrush(Color.White);
    using var dimBrush  = new SolidBrush(Color.FromArgb(160, 0, 0, 0));

    if (detection.Succeeded)
    {
        DrawRegionRect(g, detection.ChatListRect,     Color.Green, "CHAT LIST",    labelFont, textBrush, dimBrush);
        DrawRegionRect(g, detection.ConversationRect, Color.Red,   "CONVERSATION", labelFont, textBrush, dimBrush);
    }
    else
    {
        const int bandH = 40;
        g.FillRectangle(dimBrush, 0, 0, width, bandH);
        string msg = $"DETECTION FAILED: {detection.FailureReason}";
        using var failBrush = new SolidBrush(Color.Red);
        using var failFont  = new Font("Consolas", 13f, FontStyle.Bold, GraphicsUnit.Point);
        SizeF sz = g.MeasureString(msg, failFont);
        g.DrawString(msg, failFont, failBrush,
            (width - sz.Width) / 2f, (bandH - sz.Height) / 2f);
    }

    bmp.Save(path, ImageFormat.Png);
}
```

- [ ] **Step 3: Add `DrawRegionRect`**

```csharp
static void DrawRegionRect(
    Graphics g, System.Windows.Rect rect, Color strokeColor, string label,
    Font labelFont, SolidBrush textBrush, SolidBrush bgBrush)
{
    float x = (float)rect.X, y = (float)rect.Y;
    float w = (float)rect.Width, h = (float)rect.Height;

    using var pen = new Pen(strokeColor, 3f);
    g.DrawRectangle(pen, x, y, w, h);

    SizeF sz  = g.MeasureString(label, labelFont);
    float bgW = sz.Width  + 6f;
    float bgH = sz.Height + 2f;
    g.FillRectangle(bgBrush, x, y, bgW, bgH);
    g.DrawString(label, labelFont, textBrush, x + 3f, y + 1f);
}
```

- [ ] **Step 4: Verify the build compiles**

```
dotnet build tools/RegionDump/RegionDump.csproj -p:Platform=x64
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```
git add tools/RegionDump/Program.cs
git commit -m "feat(tools/region-dump): add SaveRawPng, SaveAnnotatedPng, and DrawRegionRect helpers"
```

---

### Task 7: Entry point wiring and console summary

**Files:**
- Modify: `tools/RegionDump/Program.cs`

Replace the `Console.WriteLine("RegionDump placeholder")` line with the real entry point, and add `PrintSummary` to the helpers section.

- [ ] **Step 1: Replace the placeholder line with the full entry point**

Replace:
```csharp
Console.WriteLine("RegionDump placeholder");
```

With:

```csharp
string label = args.Length > 0 ? args[0] : "unlabeled";

IntPtr hwnd = FindWhatsAppWindow();
if (hwnd == IntPtr.Zero)
{
    Console.Error.WriteLine("WhatsApp Desktop window not found.");
    return 1;
}

GraphicsCaptureItem? item = CreateCaptureItem(hwnd);
if (item is null) return 1;

IDirect3DDevice device = CreateD3DDevice();

var frameData = CaptureOneFrame(item, device);
if (frameData is null) return 1;

var (pixels, width, height, stride) = frameData.Value;

string cwd           = Environment.CurrentDirectory;
string rawPath       = Path.Combine(cwd, $"region-dump-{label}-raw.png");
string annotatedPath = Path.Combine(cwd, $"region-dump-{label}-annotated.png");

SaveRawPng(pixels, width, height, stride, rawPath);

var detector  = new WhatsAppRegionDetector();
var detection = detector.Detect(pixels, width, height, stride);

SaveAnnotatedPng(pixels, width, height, stride, detection, annotatedPath);

PrintSummary(label, width, height, detection, rawPath, annotatedPath);

return 0;
```

- [ ] **Step 2: Add `PrintSummary` to the helpers section (before the `// ── COM interfaces ──` comment)**

```csharp
static void PrintSummary(
    string label, int width, int height,
    RegionDetectionResult detection, string rawPath, string annotatedPath)
{
    Console.WriteLine($"Label:        {label}");
    Console.WriteLine($"Frame size:   {width}x{height}");

    if (detection.Succeeded)
    {
        int dividerX = (int)detection.ChatListRect.Right;  // == ConversationRect.Left
        Console.WriteLine("Detection:    SUCCEEDED");
        Console.WriteLine($"Divider x:    {dividerX}");
        Console.WriteLine($"Chat list:    ({(int)detection.ChatListRect.X}, " +
                          $"{(int)detection.ChatListRect.Y}, " +
                          $"{(int)detection.ChatListRect.Width}, " +
                          $"{(int)detection.ChatListRect.Height})");
        Console.WriteLine($"Conversation: ({(int)detection.ConversationRect.X}, " +
                          $"{(int)detection.ConversationRect.Y}, " +
                          $"{(int)detection.ConversationRect.Width}, " +
                          $"{(int)detection.ConversationRect.Height})");
    }
    else
    {
        Console.WriteLine("Detection:    FAILED");
        Console.WriteLine($"Failure reason: {detection.FailureReason}");
    }

    Console.WriteLine($"Output:       {Path.GetFileName(rawPath)}, " +
                      $"{Path.GetFileName(annotatedPath)}");
}
```

- [ ] **Step 3: Verify the final build is clean with zero warnings**

```
dotnet build tools/RegionDump/RegionDump.csproj -p:Platform=x64
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```
git add tools/RegionDump/Program.cs
git commit -m "feat(tools/region-dump): wire entry point and add PrintSummary"
```

---

### Task 8: README

**Files:**
- Create: `tools/RegionDump/README.md`

- [ ] **Step 1: Create the README**

```markdown
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
```

- [ ] **Step 2: Commit**

```
git add tools/RegionDump/README.md
git commit -m "docs(tools/region-dump): add README with usage, interpretation, and privacy warning"
```

---

### Task 9: Smoke test

No code changes. Verify the tool works end-to-end against a live WhatsApp Desktop instance.

- [ ] **Step 1: Open WhatsApp Desktop** — must be visible on-screen (not minimised)

- [ ] **Step 2: Run the tool from a scratch directory**

```
mkdir C:\Temp\region-dump-test
cd C:\Temp\region-dump-test
dotnet run --project C:\Users\moham\OneDrive\Desktop\Gausslite\tools\RegionDump\RegionDump.csproj -p:Platform=x64 -- default
```

Expected console output (exact values will differ):

```
Label:        default
Frame size:   1280x800
Detection:    SUCCEEDED
Divider x:    320
Chat list:    (0, 0, 320, 800)
Conversation: (320, 0, 960, 800)
Output:       region-dump-default-raw.png, region-dump-default-annotated.png
```

If `Detection: FAILED`, read the failure reason. Do **not** change detector code — note the observation and continue.

- [ ] **Step 3: Verify both PNGs exist in the scratch directory**

```
dir C:\Temp\region-dump-test\region-dump-default-*.png
```

Expected: two files, both non-zero in size.

- [ ] **Step 4: Open `region-dump-default-annotated.png` and eyeball it**

Checklist:
- Green rectangle outlines the chat list (left panel)
- Red rectangle outlines the conversation area (right panel)
- "CHAT LIST" and "CONVERSATION" text labels are visible at the top-left of each rectangle
- The underlying WhatsApp content is visible through the outlines (no fill)

- [ ] **Step 5: Clean up the scratch directory**

```
rmdir /s /q C:\Temp\region-dump-test
```

---

## Proposed commit message for the PR

```
feat(tools): add RegionDump visual smoke-test tool for WhatsAppRegionDetector

Standalone .NET 8 console app (tools/RegionDump/) that captures one live
WhatsApp Desktop frame via WGC, runs WhatsAppRegionDetector.Detect() on it,
and writes a raw PNG and an annotated PNG (green = chat list, red =
conversation) to the current working directory.

GPU→CPU readback uses SoftwareBitmap.CreateCopyFromSurfaceAsync (in-box,
MTA-safe). Annotation uses System.Drawing.Common (GDI+, Windows-only).
Capture has a 2s timeout that disposes the session cleanly before exit.

Outputs excluded from git via region-dump-*.png pattern.
```
