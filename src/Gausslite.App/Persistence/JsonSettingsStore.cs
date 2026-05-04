// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gausslite.App.Diagnostics;
using Gausslite.Core.Settings;

namespace Gausslite.App.Persistence;

/// <summary>
/// Persists <see cref="Settings"/> as JSON under
/// <c>%LOCALAPPDATA%\Gausslite\settings.json</c>.  Per-user, no admin required,
/// survives app reinstallation.
///
/// All errors during load (missing file, corrupt JSON, IO failure) silently
/// degrade to <c>new Settings()</c> defaults — first-run behavior — and are
/// logged via <see cref="StartupLog"/>.  Save failures return <c>false</c>
/// and are likewise logged but never thrown.
/// </summary>
internal sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    /// <summary>Default-constructs a store rooted at <c>%LOCALAPPDATA%\Gausslite\settings.json</c>.</summary>
    public JsonSettingsStore() : this(DefaultPath())
    {
    }

    /// <summary>Test seam — allows redirecting the JSON file to a temp path.</summary>
    internal JsonSettingsStore(string path)
    {
        _path = path;
    }

    public Settings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                StartupLog.Info($"JsonSettingsStore.Load: no settings file at {_path}; using defaults.");
                return new Settings();
            }

            string json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<Settings>(json, Options);
            if (loaded is null)
            {
                StartupLog.Warn($"JsonSettingsStore.Load: deserialize returned null at {_path}; using defaults.");
                return new Settings();
            }
            return loaded;
        }
        catch (Exception ex)
        {
            StartupLog.Warn($"JsonSettingsStore.Load: failed to read {_path}; using defaults.", ex);
            return new Settings();
        }
    }

    public bool Save(Settings settings)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(settings, Options);
            File.WriteAllText(_path, json);
            return true;
        }
        catch (Exception ex)
        {
            StartupLog.Warn($"JsonSettingsStore.Save: failed to write {_path}.", ex);
            return false;
        }
    }

    private static string DefaultPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "Gausslite", "settings.json");
    }
}
