// SPDX-License-Identifier: AGPL-3.0-or-later
using Gausslite.Core.Capture;
using Gausslite.Core.Diagnostics;
using Windows.Graphics.DirectX.Direct3D11;

namespace Gausslite.Core.Blur;

public sealed class BlurPipeline : IBlurPipeline
{
    // DefaultBlurRadius derives from the preset table so both stay in sync.
    public const float DefaultBlurRadius = BlurIntensityPresets.MediumRadius;

    private readonly IBlurInterop _interop;
    private bool _initialized;
    private bool _disposed;
    private IBlurCanvasDevice? _canvasDevice;

    // _renderTarget, _cachedInputFrame, and _stagingTexture are always the same pixel dimensions.
    // All three are guarded by _cacheLock: written from the capture thread (BlurFrame),
    // read from the UI thread (TryRenderCurrentFrame, TryReadLatestFrameAsBgra).
    private IBlurRenderTarget?    _renderTarget;
    private ICachedFrame?         _cachedInputFrame;
    private IBlurStagingTexture?  _stagingTexture;
    private readonly object _cacheLock = new();

    // volatile: written from UI thread (preset change), read from frame-processing thread.
    private volatile float _blurRadius = DefaultBlurRadius;
    public float BlurRadius { get => _blurRadius; set => _blurRadius = value; }

    public BlurPipeline(IBlurInterop interop)
    {
        _interop = interop;
    }

    public void Initialize(IDirect3DDevice device)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _canvasDevice = _interop.CreateCanvasDevice(device);
        _initialized = true;
    }

    public IBlurRenderTarget BlurFrame(ICaptureFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized)
            throw new InvalidOperationException("BlurPipeline has not been initialized. Call Initialize() first.");

        var (width, height) = _interop.GetFrameSize(frame);

        lock (_cacheLock)
        {
            if (_renderTarget is null || _renderTarget.Width != width || _renderTarget.Height != height)
            {
                _renderTarget?.Dispose();
                _renderTarget = _interop.CreateRenderTarget(_canvasDevice!, width, height);
                // Reallocate the input cache alongside the render target (same dimension invariant).
                _cachedInputFrame?.Dispose();
                _cachedInputFrame = _interop.CreateCachedFrame(_canvasDevice!, width, height);
                // Staging texture is also dimension-bound; discard so TryReadLatestFrameAsBgra reallocates.
                _stagingTexture?.Dispose();
                _stagingTexture = null;
            }

            _interop.DrawBlur(_canvasDevice!, _renderTarget!, frame, BlurRadius);
            _interop.UpdateCachedFrame(_canvasDevice!, _cachedInputFrame!, frame);

            return _renderTarget!;
        }
    }

    public IBlurRenderTarget? TryRenderCurrentFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_cacheLock)
        {
            if (_cachedInputFrame is null || _renderTarget is null)
            {
                DiagLog.Info("TryRenderCurrentFrame: no cached frame yet; returning null");
                return null;
            }

            DiagLog.Info($"TryRenderCurrentFrame: re-rendering {_renderTarget.Width}x{_renderTarget.Height} at radius={BlurRadius:F1} DIPs");
            _interop.DrawBlurFromCache(_canvasDevice!, _renderTarget, _cachedInputFrame, BlurRadius);

            // Flush the D3D11 command buffer so the GPU has the new blur content before
            // D3DImageBridge creates the D3D9Ex shared-surface wrapper on the same texture.
            // Without this, the synchronous UI-thread re-render path has no scheduling gap
            // and the UMD may not have submitted its pending draw commands to the GPU queue,
            // causing D3D9Ex to read stale content and WPF to composite the previous frame.
            _interop.FlushDevice(_canvasDevice!);
            DiagLog.Info($"TryRenderCurrentFrame: D3D11 context flushed at radius={BlurRadius:F1} DIPs");

            return _renderTarget;
        }
    }

    public bool TryReadLatestFrameAsBgra(out byte[] bgraPixels, out int width, out int height, out int stride)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_cacheLock)
        {
            if (_cachedInputFrame is null)
            {
                bgraPixels = Array.Empty<byte>();
                width = height = stride = 0;
                return false;
            }

            // Allocate or reallocate the staging texture when dimensions change.
            // Mirrors the _renderTarget/_cachedInputFrame reallocation pattern in BlurFrame.
            if (_stagingTexture is null ||
                _stagingTexture.Width  != _cachedInputFrame.Width ||
                _stagingTexture.Height != _cachedInputFrame.Height)
            {
                _stagingTexture?.Dispose();
                _stagingTexture = _interop.CreateStagingTexture(_canvasDevice!, _cachedInputFrame.Width, _cachedInputFrame.Height);
            }

            return _interop.TryReadBgra(_canvasDevice!, _cachedInputFrame, _stagingTexture, out bgraPixels, out width, out height, out stride);
        }
    }

    public void ClearCachedFrame()
    {
        if (_disposed) return;
        lock (_cacheLock)
        {
            // Dispose all three together: BlurFrame's allocation guard ("if _renderTarget is
            // null || dims wrong") only triggers full reallocation when the render target is
            // missing or the wrong size. Keeping _renderTarget while clearing _cachedInputFrame
            // breaks that invariant — the next BlurFrame would skip allocation and call
            // UpdateCachedFrame with a null _cachedInputFrame, throwing NullReferenceException
            // on every frame for the rest of the session.
            _renderTarget?.Dispose();
            _renderTarget = null;
            _cachedInputFrame?.Dispose();
            _cachedInputFrame = null;
            _stagingTexture?.Dispose();
            _stagingTexture = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_cacheLock)
        {
            _renderTarget?.Dispose();
            _cachedInputFrame?.Dispose();
            _stagingTexture?.Dispose();
            _renderTarget = null;
            _cachedInputFrame = null;
            _stagingTexture = null;
        }
        _canvasDevice?.Dispose();
        _canvasDevice = null;
    }
}
