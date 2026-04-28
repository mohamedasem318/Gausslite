using Windows.Graphics.Capture;

namespace WAshed.Core.Capture;

internal sealed class WinRTCaptureSession : ICaptureSession
{
    private readonly GraphicsCaptureSession _session;

    internal WinRTCaptureSession(GraphicsCaptureSession session) => _session = session;

    public void StartCapture() => _session.StartCapture();

    public void Dispose() => _session.Dispose();
}
