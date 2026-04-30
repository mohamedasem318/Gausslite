using Gausslite.Core.WindowTracking;

namespace Gausslite.Core.AppProfiles;

public sealed class WhatsAppProfile : IAppProfile
{
    private readonly IWin32Api _win32;

    public WhatsAppProfile(IWin32Api win32) => _win32 = win32;

    public string Name => "WhatsApp";

    /// <summary>
    /// Returns true if the given window belongs to WhatsApp Desktop (any install variant).
    /// Rejects WebView2 child windows (msedgewebview2 process).
    /// </summary>
    public bool IsAppWindow(string processName, string className, string title)
    {
        if (string.IsNullOrEmpty(title)) return false;
        if (processName.Contains("msedgewebview", StringComparison.OrdinalIgnoreCase)) return false;
        if (processName.StartsWith("WhatsApp", StringComparison.OrdinalIgnoreCase)) return true;
        if (className.Equals("WinUIDesktopWin32WindowClass", StringComparison.Ordinal) &&
            title.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public IntPtr FindWindowHandle() => _win32.FindWindowHandle(IsAppWindow);
}
