namespace BatchRenamePro.Core.Abstractions;

/// <summary>
/// A file name split into the part rules usually operate on (<see cref="BaseName"/>) and the
/// extension, including its leading dot.
/// </summary>
/// <remarks>
/// The split is performed here rather than through <see cref="Path.GetExtension(string)"/> so the
/// two behaviours that matter to a rename tool are explicit and testable:
/// directories never have an extension (<c>project.v2</c> is a whole folder name), and dot-files
/// such as <c>.gitignore</c> are treated as a base name with no extension.
/// </remarks>
public readonly record struct NameParts
{
    /// <summary>Creates a name from its two parts.</summary>
    /// <param name="baseName">The name without its extension.</param>
    /// <param name="extension">The extension including its leading dot, or an empty string.</param>
    public NameParts(string baseName, string extension)
    {
        BaseName = baseName ?? string.Empty;
        Extension = extension ?? string.Empty;
    }

    /// <summary>The file name without its extension.</summary>
    public string BaseName { get; init; }

    /// <summary>The extension including its leading dot, or an empty string when there is none.</summary>
    public string Extension { get; init; }

    /// <summary>The reassembled file name.</summary>
    public string FullName => BaseName + Extension;

    /// <summary>Splits a file name into its base name and extension.</summary>
    /// <param name="fileName">The file or directory name, without any directory component.</param>
    /// <param name="isDirectory">When <see langword="true"/> the whole name is treated as the base name.</param>
    public static NameParts Split(string fileName, bool isDirectory = false)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        if (isDirectory) return new NameParts(fileName, string.Empty);

        var dot = fileName.LastIndexOf('.');

        // A dot at index 0 belongs to a dot-file, and a trailing dot is not an extension either.
        if (dot <= 0 || dot == fileName.Length - 1) return new NameParts(fileName, string.Empty);

        return new NameParts(fileName[..dot], fileName[dot..]);
    }

    /// <summary>Returns the reassembled file name.</summary>
    public override string ToString() => FullName;
}
