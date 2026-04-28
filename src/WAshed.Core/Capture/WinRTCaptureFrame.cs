using Windows.Graphics.Capture;

namespace WAshed.Core.Capture;

internal sealed class WinRTCaptureFrame : ICaptureFrame
{
    private readonly Direct3D11CaptureFrame _frame;

    internal WinRTCaptureFrame(Direct3D11CaptureFrame frame) => _frame = frame;

    public Direct3D11CaptureFrame Frame => _frame;

    public void Dispose() => _frame.Dispose();
}
