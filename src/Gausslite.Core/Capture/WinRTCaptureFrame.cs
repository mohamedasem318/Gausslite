using Windows.Graphics.Capture;
using Windows.Graphics;

namespace Gausslite.Core.Capture;

internal sealed class WinRTCaptureFrame : ICaptureFrame
{
    private readonly Direct3D11CaptureFrame _frame;

    internal WinRTCaptureFrame(Direct3D11CaptureFrame frame) => _frame = frame;

    public Direct3D11CaptureFrame Frame => _frame;

    public SizeInt32 ContentSize => _frame.ContentSize;

    public void Dispose() => _frame.Dispose();
}
