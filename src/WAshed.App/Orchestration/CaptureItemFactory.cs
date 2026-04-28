using Windows.Graphics.Capture;
using WAshed.Core.WindowTracking;

namespace WAshed.App.Orchestration;

/// <summary>
/// Locates WhatsApp Desktop's HWND and creates a <see cref="GraphicsCaptureItem"/> for it.
/// </summary>
internal sealed class CaptureItemFactory : ICaptureItemFactory
{
    private readonly IWin32Api _win32Api;

    public CaptureItemFactory(IWin32Api win32Api) => _win32Api = win32Api;

    public bool TryCreateForWhatsApp(out GraphicsCaptureItem? item)
    {
        item = null;

        var handles = _win32Api.GetWindowHandlesForProcessName("WhatsApp");
        if (handles.Count == 0)
            handles = _win32Api.GetWindowHandlesForProcessName("WhatsAppDesktop");
        if (handles.Count == 0)
            return false;

        // TODO (next session): create GraphicsCaptureItem from the HWND via
        // IGraphicsCaptureItemInterop using P/Invoke to RoGetActivationFactory.
        // WindowsRuntimeMarshal.GetActivationFactory was removed in .NET 6+.
        return false;
    }
}
