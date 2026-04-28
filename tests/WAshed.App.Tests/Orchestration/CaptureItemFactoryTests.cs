using WAshed.App.Orchestration;
using WAshed.Core.WindowTracking;

namespace WAshed.App.Tests.Orchestration;

/// <summary>
/// Integration-style tests for <see cref="CaptureItemFactory"/> using the real Win32 API.
/// These tests do NOT require WhatsApp to be installed.
/// </summary>
public sealed class CaptureItemFactoryTests
{
    /// <summary>
    /// Validates the "no-throw on absent target" contract: when WhatsApp is not running,
    /// <see cref="CaptureItemFactory.TryCreateForWhatsApp"/> must return <see langword="false"/>
    /// with a null item and must not throw any exception.
    /// This covers both the "WhatsApp not found" path and the "WGC not supported" early-exit path.
    /// </summary>
    [Fact]
    public void TryCreateForWhatsApp_WhenWhatsAppNotRunning_ReturnsFalseAndNull()
    {
        var factory = new CaptureItemFactory(new Win32Api());

        bool result = factory.TryCreateForWhatsApp(out var item);

        Assert.False(result);
        Assert.Null(item);
    }

    /// <summary>
    /// Calling <see cref="CaptureItemFactory.TryCreateForWhatsApp"/> multiple times must
    /// never throw, even if the first call already returned false.
    /// </summary>
    [Fact]
    public void TryCreateForWhatsApp_CalledTwice_NeverThrows()
    {
        var factory = new CaptureItemFactory(new Win32Api());

        var ex = Record.Exception(() =>
        {
            factory.TryCreateForWhatsApp(out _);
            factory.TryCreateForWhatsApp(out _);
        });

        Assert.Null(ex);
    }
}
