// SPDX-License-Identifier: AGPL-3.0-or-later
using Windows.Graphics.Capture;

namespace Gausslite.Core.Capture;

internal sealed class WinRTCaptureSession : ICaptureSession
{
    private readonly GraphicsCaptureSession _session;

    internal WinRTCaptureSession(GraphicsCaptureSession session) => _session = session;

    public void StartCapture() => _session.StartCapture();

    public bool IsBorderRequired
    {
        set => _session.IsBorderRequired = value;
    }

    public void Dispose() => _session.Dispose();
}
