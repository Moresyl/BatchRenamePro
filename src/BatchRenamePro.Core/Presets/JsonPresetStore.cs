using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BatchRenamePro.Core.Presets;

/// <summary>Saves and loads user-authored rule pipelines.</summary>
public interface IPresetStore
{
    /// <summary>Reads every saved preset, sorted by name.</summary>
    Task<IReadOnlyList<RulePreset>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes a preset, replacing any existing one with the same name.</summary>
    Task SaveAsync(RulePreset preset, CancellationToken cancellationToken = default);

    /// <summary>Removes a preset by name.</summary>
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Writes a preset to an arbitrary path, for sharing between machines.</summary>
    Task ExportAsync(RulePreset preset, string path, CancellationToken cancellationToken = default);

    /// <summary>Reads a preset from an arbitrary path.</summary>
    Task<RulePreset> ImportAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>A preset store backed by one JSON file per preset.</summary>
/// <remarks>
/// One file per preset makes a preset a shareable artifact: it can be attached to an issue, checked
/// into a team repository, or dropped into the folder by hand. The polymorphic <c>$type</c>
/// discriminators on each rule are part of that contract, which is why they are declared explicitly
/// rather than derived from type names.
/// </remarks>
public sealed class JsonPresetStore : IPresetStore
{
    private const string FileExtension = ".preset.json";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },

        // Preset names and rule text are frequently CJK; escaping them would make the file unreadable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _directoryPath;

    /// <summary>Creates a store rooted at <paramref name="directoryPath"/>.</summary>
    public JsonPresetStore(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        _directoryPath = directoryPath;
    }

    /// <summary>The default location: <c>%LOCALAPPDATA%\BatchRenamePro\Presets</c>.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BatchRenamePro",
        "Presets");

    /// <inheritdoc />
    public async Task<IReadOnlyList<RulePreset>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_directoryPath)) return [];

        var presets = new List<RulePreset>();

        foreach (var path in Directory.EnumerateFiles(_directoryPath, "*" + FileExtension))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                presets.Add(await ReadAsync(path, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                // A preset saved by a newer version, or a hand-edited file with a typo, must not stop
                // the rest from loading.
            }
        }

        return [.. presets.OrderBy(preset => preset.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <inheritdoc />
    public async Task SaveAsync(RulePreset preset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (preset.IsBuiltIn) throw new InvalidOperationException("Built-in presets cannot be overwritten.");

        Directory.CreateDirectory(_directoryPath);
        await WriteAsync(preset, Path.Combine(_directoryPath, Sanitize(preset.Name) + FileExtension), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            File.Delete(Path.Combine(_directoryPath, Sanitize(name) + FileExtension));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Already gone, or locked; neither is worth failing over.
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ExportAsync(RulePreset preset, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return WriteAsync(preset with { IsBuiltIn = false }, path, cancellationToken);
    }

    /// <inheritdoc />
    public Task<RulePreset> ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ReadAsync(path, cancellationToken);
    }

    private static async Task WriteAsync(RulePreset preset, string path, CancellationToken cancellationToken)
    {
        var temporaryPath = path + ".tmp";

        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, preset, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    private static async Task<RulePreset> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        var preset = await JsonSerializer.DeserializeAsync<RulePreset>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);

        return preset is null
            ? throw new JsonException($"The preset file is empty: {path}")
            : preset with { IsBuiltIn = false };
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. name.Select(character => Array.IndexOf(invalid, character) < 0 ? character : '_')]).Trim();
        return cleaned.Length == 0 ? "preset" : cleaned;
    }
}
