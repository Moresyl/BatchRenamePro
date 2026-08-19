using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace BatchRenamePro.App.Services;

/// <summary>Loads and saves <see cref="AppSettings"/>.</summary>
public interface ISettingsService
{
    /// <summary>The live settings object. Mutate it, then call <see cref="Save"/>.</summary>
    AppSettings Current { get; }

    /// <summary>The folder holding settings, presets and history.</summary>
    string DataDirectory { get; }

    /// <summary>Raised after settings are replaced wholesale, as by <see cref="Reset"/>.</summary>
    event EventHandler? Reloaded;

    /// <summary>Writes the current settings to disk. Safe to call often.</summary>
    void Save();

    /// <summary>Throws the settings away and starts again from the defaults.</summary>
    void Reset();
}

/// <inheritdoc cref="ISettingsService" />
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly ILogger<SettingsService> _logger;

    /// <summary>Creates the service and loads whatever is on disk.</summary>
    /// <param name="logger">Where load and save failures are reported.</param>
    /// <param name="dataDirectory">Where the settings file lives; defaults to the per-user app data folder.</param>
    public SettingsService(ILogger<SettingsService> logger, string? dataDirectory = null)
    {
        _logger = logger;
        DataDirectory = dataDirectory ?? DefaultDataDirectory;
        _path = Path.Combine(DataDirectory, "settings.json");
        Current = Load();
    }

    /// <summary>Where the application keeps everything, under the roaming profile.</summary>
    public static string DefaultDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BatchRenamePro");

    /// <inheritdoc />
    public AppSettings Current { get; private set; }

    /// <inheritdoc />
    public string DataDirectory { get; }

    /// <inheritdoc />
    public event EventHandler? Reloaded;

    /// <inheritdoc />
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);

            // Written beside the real file and moved into place, so a crash mid-write leaves the
            // previous settings intact instead of a truncated file that fails to parse next launch.
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(Current, Format));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Settings are a convenience. Losing them is not worth interrupting the user over.
            _logger.LogWarning(error, "Could not write settings to {Path}", _path);
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        Current = new AppSettings();
        Save();
        Reloaded?.Invoke(this, EventArgs.Empty);
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Format);
                if (loaded is not null)
                {
                    loaded.Normalize();
                    return loaded;
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(error, "Could not read settings from {Path}; falling back to defaults", _path);
        }

        return new AppSettings();
    }
}
