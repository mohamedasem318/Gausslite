// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO;
using Gausslite.App.Persistence;
using Gausslite.Core.Blur;
using Gausslite.Core.Settings;

namespace Gausslite.App.Tests.Persistence;

/// <summary>
/// Tests for <see cref="JsonSettingsStore"/>.  All tests use a temp-file path
/// (the internal ctor) so they never touch the real %LOCALAPPDATA% folder.
/// </summary>
public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _tempPath;

    public JsonSettingsStoreTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"gausslite-test-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempPath)) File.Delete(_tempPath);
        // Also clean up any directory that Save might have created.
        var parent = Path.GetDirectoryName(_tempPath);
        if (parent is not null && Directory.Exists(parent) && parent != Path.GetTempPath())
        {
            try { Directory.Delete(parent, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsDefaults()
    {
        var store = new JsonSettingsStore(_tempPath);

        var settings = store.Load();

        Assert.Equal(BlurIntensityPreset.Medium, settings.Intensity);
        Assert.Equal(BlurRegionScope.Both, settings.Scope);
        Assert.False(settings.AutoStart);
        Assert.False(settings.ProcessRunningHeuristicEnabled);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var original = new Settings
        {
            Intensity = BlurIntensityPreset.Heavy,
            Scope = BlurRegionScope.Conversation,
            AutoStart = true,
            ProcessRunningHeuristicEnabled = true,
        };

        var store = new JsonSettingsStore(_tempPath);
        Assert.True(store.Save(original));

        var loaded = store.Load();

        Assert.Equal(original.Intensity, loaded.Intensity);
        Assert.Equal(original.Scope, loaded.Scope);
        Assert.Equal(original.AutoStart, loaded.AutoStart);
        Assert.Equal(original.ProcessRunningHeuristicEnabled, loaded.ProcessRunningHeuristicEnabled);
    }

    [Fact]
    public void Load_WhenFileIsCorruptJson_ReturnsDefaultsAndDoesNotThrow()
    {
        File.WriteAllText(_tempPath, "{ this is not valid JSON ");

        var store = new JsonSettingsStore(_tempPath);
        var settings = store.Load();

        // Defaults
        Assert.Equal(BlurIntensityPreset.Medium, settings.Intensity);
        Assert.False(settings.AutoStart);
    }

    [Fact]
    public void Load_WhenFileIsEmpty_ReturnsDefaults()
    {
        File.WriteAllText(_tempPath, "");

        var store = new JsonSettingsStore(_tempPath);
        var settings = store.Load();

        Assert.Equal(BlurIntensityPreset.Medium, settings.Intensity);
    }

    [Fact]
    public void Load_WhenFileIsLiteralNullJson_ReturnsDefaults()
    {
        File.WriteAllText(_tempPath, "null");

        var store = new JsonSettingsStore(_tempPath);
        var settings = store.Load();

        Assert.Equal(BlurIntensityPreset.Medium, settings.Intensity);
    }

    [Fact]
    public void Save_CreatesParentDirectoryIfMissing()
    {
        // Path with a never-yet-created parent directory.
        var nestedPath = Path.Combine(
            Path.GetTempPath(),
            $"gausslite-nested-{Guid.NewGuid():N}",
            "settings.json");

        try
        {
            var store = new JsonSettingsStore(nestedPath);
            Assert.True(store.Save(new Settings { AutoStart = true }));
            Assert.True(File.Exists(nestedPath));
        }
        finally
        {
            var parent = Path.GetDirectoryName(nestedPath);
            if (parent is not null && Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void Load_WhenFileHasOnlySomeFields_FillsRestWithDefaults()
    {
        // Only Intensity is set; Scope/AutoStart/ProcessRunningHeuristicEnabled
        // should fall back to their record defaults.
        File.WriteAllText(_tempPath, "{ \"intensity\": \"Heavy\" }");

        var store = new JsonSettingsStore(_tempPath);
        var settings = store.Load();

        Assert.Equal(BlurIntensityPreset.Heavy, settings.Intensity);
        Assert.Equal(BlurRegionScope.Both, settings.Scope);
        Assert.False(settings.AutoStart);
        Assert.False(settings.ProcessRunningHeuristicEnabled);
    }
}
