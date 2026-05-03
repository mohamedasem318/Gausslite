// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Gausslite.Core.Blur;

public enum BlurIntensityPreset
{
    Light = 0,
    Medium = 1,
    Heavy = 2,
}

/// <summary>
/// Maps <see cref="BlurIntensityPreset"/> values to Gaussian blur radii in DIPs.
/// Single source of truth for preset numeric values; future settings and slider
/// code should reference these constants rather than repeating the literal values.
/// </summary>
public static class BlurIntensityPresets
{
    public const float LightRadius  = 10.0f;
    public const float MediumRadius = 20.0f;
    public const float HeavyRadius  = 35.0f;

    public static float ToRadius(BlurIntensityPreset preset) => preset switch
    {
        BlurIntensityPreset.Light  => LightRadius,
        BlurIntensityPreset.Medium => MediumRadius,
        BlurIntensityPreset.Heavy  => HeavyRadius,
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
    };
}
