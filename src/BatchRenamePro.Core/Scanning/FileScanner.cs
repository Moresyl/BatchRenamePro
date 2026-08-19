using System.Text.RegularExpressions;
using BatchRenamePro.Core.Abstractions;
using BatchRenamePro.Core.Planning;
using BatchRenamePro.Core.Sorting;

namespace BatchRenamePro.Core.Scanning;

/// <summary>What a scan collects.</summary>
public enum ScanTarget
{
    /// <summary>Files only.</summary>
    Files,

    /// <summary>Directories only.</summary>
    Directories,

    /// <summary>Both.</summary>
    Both
}

/// <summary>How a folder is expanded into a list of items to rename.</summary>
/// <param name="Target">Whether to collect files, folders or both.</param>
/// <param name="Recursive">Whether to descend into subfolders.</param>
/// <param name="MaxDepth">How deep to descend; 0 means the folder itself only.</param>
/// <param name="IncludePatterns">Wildcard filters an item must match, for example <c>*.jpg;*.png</c>. Empty accepts everything.</param>
/// <param name="ExcludePatterns">Wildcard filters that reject an item, applied after the include filters.</param>
/// <param name="IncludeHidden">Whether hidden and system items are collected.</param>
public readonly record struct ScanOptions(
    ScanTarget Target = ScanTarget.Files,
    bool Recursive = false,
    int MaxDepth = 16,
    string IncludePatterns = "",
    string ExcludePatterns = "",
    bool IncludeHidden = false)
{
    /// <summary>Defaults: the folder's own files, no recursion, nothing hidden.</summary>
    /// <remarks>
    /// Spelled out rather than written <c>new()</c>: a record struct's parameterless constructor
    /// zero-initialises instead of applying the primary-constructor defaults, which would leave
    /// <see cref="MaxDepth"/> at 0. For the same reason, never write <c>default(ScanOptions)</c>.
    /// </remarks>
    public static ScanOptions Default { get; } = new(
        Target: ScanTarget.Files,
        Recursive: false,
        MaxDepth: 16,
        IncludePatterns: "",
        ExcludePatterns: "",
        IncludeHidden: false);
}

/// <summary>
/// Expands folders into the items a batch will act on, applying wildcard filters and depth limits.
/// </summary>
/// <remarks>
/// Directories are yielded after their contents when recursing. That ordering matters: renaming a
/// folder invalidates the paths of everything inside it, so the children have to be dealt with while
/// their paths are still valid.
/// </remarks>
public interface IFileScanner
{
    /// <summary>Collects the items under <paramref name="rootPath"/> that match <paramref name="options"/>.</summary>
    /// <param name="rootPath">The folder to expand.</param>
    /// <param name="options">Filters and depth limits; <see cref="ScanOptions.Default"/> when omitted.</param>
    /// <param name="cancellationToken">Cancels a long scan.</param>
    IReadOnlyList<RenameSource> Scan(string rootPath, ScanOptions? options = null, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IFileScanner" />
public sealed class FileScanner : IFileScanner
{
    private static readonly char[] PatternSeparators = [';', ','];

    /// <inheritdoc />
    public IReadOnlyList<RenameSource> Scan(string rootPath, ScanOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        if (!Directory.Exists(rootPath)) return [];

        var settings = options ?? ScanOptions.Default;
        var include = CompilePatterns(settings.IncludePatterns);
        var exclude = CompilePatterns(settings.ExcludePatterns);
        var results = new List<RenameSource>();

        Walk(new DirectoryInfo(rootPath), 0);
        return results;

        void Walk(DirectoryInfo directory, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = directory.EnumerateFileSystemInfos();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // An unreadable subfolder is skipped rather than aborting the whole scan.
                return;
            }

            var ordered = entries
                .OrderBy(entry => entry.Name, NaturalStringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var entry in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!settings.IncludeHidden && entry.Attributes.HasFlag(FileAttributes.Hidden)) continue;

                if (entry is DirectoryInfo child)
                {
                    if (settings.Recursive && depth < settings.MaxDepth) Walk(child, depth + 1);

                    if (settings.Target is ScanTarget.Directories or ScanTarget.Both && Matches(child.Name, include, exclude))
                        results.Add(new RenameSource(child.FullName, true, FileFacts.Read(child.FullName, true)));

                    continue;
                }

                if (settings.Target is ScanTarget.Files or ScanTarget.Both && Matches(entry.Name, include, exclude))
                {
                    var file = (FileInfo)entry;
                    results.Add(new RenameSource(file.FullName, false, new FileFacts(file.CreationTime, file.LastWriteTime, file.Length)));
                }
            }
        }
    }

    /// <summary>Whether a name passes the include and exclude filters.</summary>
    public static bool Matches(string name, IReadOnlyList<Regex> include, IReadOnlyList<Regex> exclude)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(include);
        ArgumentNullException.ThrowIfNull(exclude);

        if (include.Count > 0 && !include.Any(pattern => pattern.IsMatch(name))) return false;
        return !exclude.Any(pattern => pattern.IsMatch(name));
    }

    /// <summary>Turns a <c>*.jpg;*.png</c> style filter list into anchored regular expressions.</summary>
    public static IReadOnlyList<Regex> CompilePatterns(string patterns)
    {
        if (string.IsNullOrWhiteSpace(patterns)) return [];

        var compiled = new List<Regex>();

        foreach (var raw in patterns.Split(PatternSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var expression = "^" + Regex.Escape(raw).Replace(@"\*", ".*", StringComparison.Ordinal).Replace(@"\?", ".", StringComparison.Ordinal) + "$";

            try
            {
                compiled.Add(new Regex(expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
            }
            catch (ArgumentException)
            {
                // A filter that cannot be compiled is ignored rather than rejecting the whole list.
            }
        }

        return compiled;
    }
}
