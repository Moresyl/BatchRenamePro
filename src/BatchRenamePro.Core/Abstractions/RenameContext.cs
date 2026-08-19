namespace BatchRenamePro.Core.Abstractions;

/// <summary>Facts about an item on disk that rules and tokens can substitute into a name.</summary>
/// <param name="CreatedAt">Creation time in local time.</param>
/// <param name="ModifiedAt">Last write time in local time.</param>
/// <param name="SizeInBytes">Size in bytes; zero for directories.</param>
public readonly record struct FileFacts(DateTimeOffset CreatedAt, DateTimeOffset ModifiedAt, long SizeInBytes)
{
    /// <summary>Facts for an item that could not be inspected.</summary>
    public static FileFacts Unknown { get; } = new(default, default, 0);

    /// <summary>Reads creation time, write time and size from disk, returning <see cref="Unknown"/> on failure.</summary>
    /// <param name="path">The full path to inspect.</param>
    /// <param name="isDirectory">Whether the path refers to a directory.</param>
    public static FileFacts Read(string path, bool isDirectory)
    {
        try
        {
            if (isDirectory)
            {
                var directory = new DirectoryInfo(path);
                return new FileFacts(directory.CreationTime, directory.LastWriteTime, 0);
            }

            var file = new FileInfo(path);
            return new FileFacts(file.CreationTime, file.LastWriteTime, file.Exists ? file.Length : 0);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Unknown;
        }
    }
}

/// <summary>
/// Everything a rule needs to know about the item being renamed and its position in the batch.
/// </summary>
/// <remarks>
/// <see cref="Timestamp"/> is captured once per batch rather than read per item, so a pattern
/// containing <c>{time}</c> produces one consistent value across the whole operation instead of
/// drifting between the first and last file.
/// </remarks>
public sealed class RenameContext
{
    private readonly Lazy<FileFacts> _facts;

    /// <summary>Creates a context for one item in a batch.</summary>
    /// <param name="sourcePath">Full path of the item as it exists on disk.</param>
    /// <param name="isDirectory">Whether the item is a directory.</param>
    /// <param name="index">Zero-based position of the item within the batch.</param>
    /// <param name="total">Number of items in the batch.</param>
    /// <param name="timestamp">Batch timestamp shared by every item.</param>
    /// <param name="facts">Pre-read file facts; when omitted they are read lazily from disk.</param>
    public RenameContext(
        string sourcePath,
        bool isDirectory,
        int index,
        int total,
        DateTimeOffset timestamp,
        FileFacts? facts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(total);

        SourcePath = sourcePath;
        IsDirectory = isDirectory;
        Index = index;
        Total = total;
        Timestamp = timestamp;
        OriginalName = Path.GetFileName(sourcePath);
        DirectoryPath = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        _facts = facts is { } known
            ? new Lazy<FileFacts>(known)
            : new Lazy<FileFacts>(() => FileFacts.Read(sourcePath, isDirectory));
    }

    /// <summary>Full path of the item as it currently exists on disk.</summary>
    public string SourcePath { get; }

    /// <summary>The item's current name, without any directory component.</summary>
    public string OriginalName { get; }

    /// <summary>The directory containing the item.</summary>
    public string DirectoryPath { get; }

    /// <summary>The name of the containing directory, or an empty string at a drive root.</summary>
    public string ParentName => Path.GetFileName(DirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    /// <summary>Whether the item is a directory.</summary>
    public bool IsDirectory { get; }

    /// <summary>Zero-based position of the item within the batch.</summary>
    public int Index { get; }

    /// <summary>Number of items in the batch.</summary>
    public int Total { get; }

    /// <summary>Timestamp shared by every item in the batch.</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>Creation time, write time and size, read from disk on first use.</summary>
    public FileFacts Facts => _facts.Value;

    /// <summary>The item's original name split into base name and extension.</summary>
    public NameParts OriginalParts => NameParts.Split(OriginalName, IsDirectory);
}
