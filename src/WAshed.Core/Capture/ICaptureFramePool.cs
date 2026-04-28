namespace WAshed.Core.Capture;

/// <summary>
/// Thin mockable wrapper over <c>Direct3D11CaptureFramePool</c>.
/// </summary>
public interface ICaptureFramePool : IDisposable
{
    /// <summary>Raised on the thread-pool thread when a new frame is ready.</summary>
    event EventHandler? FrameArrived;

    /// <summary>Returns the next available frame, or <see langword="null"/> if none is ready.</summary>
    ICaptureFrame? TryGetNextFrame();
}
