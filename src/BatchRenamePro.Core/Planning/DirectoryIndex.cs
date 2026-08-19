namespace BatchRenamePro.Core.Planning;

/// <summary>
/// A reusable, case-insensitive snapshot of the names already present in each directory the batch
/// touches.
/// </summary>
/// <remarks>
/// Collision detection needs to answer "does this name already exist here?" once per item, and the
/// preview re-runs on every keystroke. Asking the file system directly means one
/// <see cref="File.Exists(string)"/> round trip per item per keystroke; enumerating each directory
/// once and answering from memory turns that into a single listing per directory.
/// The caller owns the lifetime and calls <see cref="Invalidate()"/> after anything writes to disk.
/// </remarks>
public sealed class DirectoryIndex
{
    private readonly Dictionary<string, HashSet<string>?> _directories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="directory"/> already contains an entry named <paramref name="name"/>.</summary>
    /// <remarks>
    /// A directory that cannot be listed reports <see langword="false"/> so the planner falls back to
    /// treating the name as free; the executor still refuses to overwrite anything it finds.
    /// </remarks>
    public bool Contains(string directory, string name)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(name);

        var names = GetNames(directory);
        return names is not null && names.Contains(name);
    }

    /// <summary>Records a name as taken without re-reading the directory.</summary>
    public void Add(string directory, string name)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(name);

        GetNames(directory)?.Add(name);
    }

    /// <summary>Drops every cached listing, forcing the next query to re-read from disk.</summary>
    public void Invalidate() => _directories.Clear();

    /// <summary>Drops the cached listing for one directory.</summary>
    public void Invalidate(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        _directories.Remove(directory);
    }

    private HashSet<string>? GetNames(string directory)
    {
        if (_directories.TryGetValue(directory, out var cached)) return cached;

        HashSet<string>? names;
        try
        {
            names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                names.Add(Path.GetFileName(path));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            names = null;
        }

        _directories[directory] = names;
        return names;
    }
}
