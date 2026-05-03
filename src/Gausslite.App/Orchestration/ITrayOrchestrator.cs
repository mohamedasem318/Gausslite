// SPDX-License-Identifier: AGPL-3.0-or-later
using Gausslite.Core.Blur;

namespace Gausslite.App.Orchestration;

public interface ITrayOrchestrator : IDisposable
{
    /// <summary>Fires after <see cref="IsBlurEnabled"/> changes. Argument is the new state.</summary>
    event EventHandler<bool>? BlurStateChanged;

    bool IsBlurEnabled { get; }
    BlurIntensityPreset CurrentIntensity { get; }
    BlurRegionScope CurrentScope { get; }

    void ToggleBlur();
    void EnableBlur();
    void DisableBlur();
    void SetIntensity(BlurIntensityPreset preset);
    void SetScope(BlurRegionScope scope);
}
