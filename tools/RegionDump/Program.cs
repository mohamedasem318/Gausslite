using System.IO;
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

// ── Helpers ───────────────────────────────────────────────────────────────────

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
        out IntPtr deviceContext);
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
        if (deviceContext != IntPtr.Zero) Marshal.Release(deviceContext);
        Marshal.Release(d3dDevicePtr);
    }
}

static (byte[] pixels, int width, int height, int stride)? CaptureOneFrame(
    GraphicsCaptureItem item, IDirect3DDevice device)
{
    // Use ICaptureInterop directly (not CaptureEngine) so we can own the frame
    // reference and do the SoftwareBitmap GPU→CPU readback on the calling thread
    // rather than inside the FrameArrived callback.
    //
    // Why: CaptureEngine disposes the frame in its own callback after Invoke()
    // returns, so doing SoftwareBitmap.CreateCopyFromSurfaceAsync inside the
    // callback and blocking it with .GetResult() deadlocks — the WinRT async
    // completion needs a free thread, but the callback thread is blocked.
    // With this approach the handler just signals; readback runs on the caller.
    var interop  = new WinRTCaptureInterop();
    var pool     = interop.CreateFreeThreadedFramePool(device, item);
    var session  = interop.CreateSession(pool, item);
    session.IsBorderRequired = false;

    ICaptureFrame? captured = null;
    var gate = new System.Threading.ManualResetEventSlim(false);

    pool.FrameArrived += (_, _) =>
    {
        if (captured != null) return;
        var frame = pool.TryGetNextFrame();
        if (frame is null) return;
        captured = frame;  // take ownership; caller disposes
        gate.Set();
    };

    session.StartCapture();
    bool arrived = gate.Wait(TimeSpan.FromSeconds(2));
    session.Dispose();  // stop capture; always runs, even on timeout

    if (!arrived)
    {
        pool.Dispose();
        Console.Error.WriteLine(
            "Capture timed out after 2s — WhatsApp window may be minimized or off-screen");
        return null;
    }

    // Readback on the calling (main) thread — MTA, no sync context.
    // SoftwareBitmap.CreateCopyFromSurfaceAsync completes on a free thread-pool
    // thread; the main thread's .GetResult() unblocks when it does.
    try
    {
        var softBitmap = SoftwareBitmap
            .CreateCopyFromSurfaceAsync(captured!.Frame.Surface)
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
            return (pixels, desc.Width, desc.Height, desc.Stride);
        }
    }
    finally
    {
        captured?.Dispose();
        pool.Dispose();
    }
}

static void SaveRawPng(byte[] pixels, int width, int height, int stride, string path)
{
    using var bmp   = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    var bmpData     = bmp.LockBits(
        new Rectangle(0, 0, width, height),
        ImageLockMode.WriteOnly,
        PixelFormat.Format32bppArgb);
    for (int y = 0; y < height; y++)
        Marshal.Copy(pixels, y * stride,
            IntPtr.Add(bmpData.Scan0, y * bmpData.Stride), width * 4);
    bmp.UnlockBits(bmpData);
    bmp.Save(path, ImageFormat.Png);
}

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
