namespace Gausslite.Core.Capture;

/// <summary>
/// Thin mockable wrapper over <c>GraphicsCaptureSession</c>.
/// </summary>
public interface ICaptureSession : IDisposable
{
    void StartCapture();
}
