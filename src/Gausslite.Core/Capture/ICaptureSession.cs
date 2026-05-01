namespace Gausslite.Core.Capture;

/// <summary>
/// Thin mockable wrapper over <c>GraphicsCaptureSession</c>.
/// </summary>
public interface ICaptureSession : IDisposable
{
    void StartCapture();

    /// <summary>
    /// When set to <c>false</c>, suppresses the system-drawn yellow capture-indicator border
    /// that Windows 11 22H2+ (build 22621) renders around the captured window.
    /// Silently ignored on older OS versions.
    /// </summary>
    bool IsBorderRequired { set; }
}
