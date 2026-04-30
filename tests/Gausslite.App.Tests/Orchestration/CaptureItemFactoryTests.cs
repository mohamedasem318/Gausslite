using System.Diagnostics;
using Gausslite.App.Orchestration;
using Gausslite.Core.AppProfiles;
using Gausslite.Core.WindowTracking;

namespace Gausslite.App.Tests.Orchestration;

/// <summary>
/// Tests for <see cref="CaptureItemFactory"/>. Integration-style tests use the real Win32 API
/// and do not require WhatsApp to be installed. Predicate tests have moved to
/// <c>WhatsAppProfileTests</c> in Gausslite.Core.Tests.
/// </summary>
public sealed class CaptureItemFactoryTests
{
    // ── Integration tests (real Win32 API, no WhatsApp required) ─────────────

    /// <summary>
    /// When the profile's app is not running, TryCreateForProfile must return false with a null item
    /// and must not throw.
    /// </summary>
    [Fact]
    public void TryCreateForProfile_WhenAppNotRunning_ReturnsFalseAndNull()
    {
        bool whatsAppRunning = Process.GetProcesses()
            .Any(p => p.ProcessName.StartsWith("WhatsApp", StringComparison.OrdinalIgnoreCase));
        if (whatsAppRunning) return; // precondition not met on this machine; skip

        var factory = new CaptureItemFactory(new WhatsAppProfile(new Win32Api()));

        bool result = factory.TryCreateForProfile(out var item);

        Assert.False(result);
        Assert.Null(item);
    }

    /// <summary>
    /// Calling TryCreateForProfile multiple times must never throw, even after a false return.
    /// </summary>
    [Fact]
    public void TryCreateForProfile_CalledTwice_NeverThrows()
    {
        var factory = new CaptureItemFactory(new WhatsAppProfile(new Win32Api()));

        var ex = Record.Exception(() =>
        {
            factory.TryCreateForProfile(out _);
            factory.TryCreateForProfile(out _);
        });

        Assert.Null(ex);
    }
}
