namespace BatchRenamePro.Core.Planning;

/// <summary>Why a proposed file name cannot be used.</summary>
/// <param name="Code">Stable identifier the presentation layer localizes.</param>
/// <param name="Message">English fallback message.</param>
public sealed record NameProblem(string Code, string Message);

/// <summary>Checks a proposed name against the rules Windows actually enforces.</summary>
/// <remarks>
/// These checks exist because the failure modes are quiet rather than loud: Windows silently strips
/// a trailing dot or space, and a file named <c>NUL</c> or <c>COM1</c> is accepted by some APIs and
/// rejected by others. Catching them in the preview is far better than discovering them halfway
/// through a batch.
/// </remarks>
public static class FileNameValidator
{
    /// <summary>The longest a single path component may be on NTFS.</summary>
    public const int MaxComponentLength = 255;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>Returns the first problem with <paramref name="fileName"/>, or <see langword="null"/> when it is usable.</summary>
    public static NameProblem? Validate(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return new NameProblem("name.empty", "The name cannot be empty.");

        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return new NameProblem("name.invalidChars", "The name contains characters Windows does not allow.");

        // Windows trims these on creation, so the file would silently get a different name.
        if (fileName.EndsWith(' ') || fileName.EndsWith('.'))
            return new NameProblem("name.trailingDotOrSpace", "The name cannot end with a space or a period.");

        if (fileName.StartsWith(' '))
            return new NameProblem("name.leadingSpace", "The name cannot start with a space.");

        if (fileName.Length > MaxComponentLength)
            return new NameProblem("name.tooLong", $"The name is longer than {MaxComponentLength} characters.");

        // "NUL" and "NUL.txt" are both reserved; only the part before the first dot matters.
        var stem = fileName.Split('.', 2)[0];
        if (ReservedNames.Contains(stem))
            return new NameProblem("name.reserved", "The name is reserved by Windows and cannot be used.");

        return null;
    }

    /// <summary>Whether a name is a Windows device name such as <c>CON</c> or <c>LPT1</c>.</summary>
    public static bool IsReserved(string stem) => ReservedNames.Contains(stem);
}
