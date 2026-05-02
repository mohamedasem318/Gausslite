using System.Runtime.InteropServices;

namespace Gausslite.Core.Blur;

/// <summary>
/// Wraps a raw D3D11 staging-texture COM pointer owned by this object.
/// Created by <see cref="Win2DBlurInterop.CreateStagingTexture"/>; disposed by
/// <see cref="BlurPipeline"/> when dimensions change or on final teardown.
/// </summary>
internal sealed class Win2DBlurStagingTexture : IBlurStagingTexture
{
    internal IntPtr TexturePtr { get; }
    public float Width  { get; }
    public float Height { get; }
    private bool _disposed;

    internal Win2DBlurStagingTexture(IntPtr texturePtr, float width, float height)
    {
        TexturePtr = texturePtr;
        Width  = width;
        Height = height;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (TexturePtr != IntPtr.Zero)
            Marshal.Release(TexturePtr);
    }
}
