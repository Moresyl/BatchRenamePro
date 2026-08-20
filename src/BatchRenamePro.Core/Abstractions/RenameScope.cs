namespace BatchRenamePro.Core.Abstractions;

/// <summary>Which part of a file name a rule is allowed to change.</summary>
public enum RenameScope
{
    /// <summary>Only the part before the extension.</summary>
    BaseName,

    /// <summary>Only the extension, excluding its leading dot.</summary>
    Extension,

    /// <summary>The whole name, extension included.</summary>
    FullName
}

/// <summary>Helpers for applying a transform to the part of a name selected by a <see cref="RenameScope"/>.</summary>
public static class RenameScopeExtensions
{
    /// <summary>
    /// Applies <paramref name="transform"/> to the portion of <paramref name="parts"/> selected by
    /// <paramref name="scope"/> and returns the recombined name.
    /// </summary>
    /// <param name="scope">Which part of the name the transform is allowed to see.</param>
    /// <param name="parts">The name being rewritten.</param>
    /// <param name="context">The item being renamed; consulted for whether it is a folder.</param>
    /// <param name="transform">The rewrite to apply.</param>
    /// <remarks>
    /// For <see cref="RenameScope.Extension"/> the transform sees the extension without its leading
    /// dot, so rules never have to special-case it. For <see cref="RenameScope.FullName"/> the
    /// result is re-split, which lets a rule legitimately move the extension boundary — and the
    /// context is what stops that re-split from inventing an extension for a folder. A folder called
    /// <c>project.v2</c> is a name with a dot in it, and once one rule in the pipeline has decided
    /// otherwise every rule after it would leave <c>.v2</c> alone.
    /// </remarks>
    public static NameParts Transform(this RenameScope scope, NameParts parts, RenameContext context, Func<string, string> transform)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(transform);

        switch (scope)
        {
            case RenameScope.BaseName:
                return parts with { BaseName = transform(parts.BaseName) ?? string.Empty };

            case RenameScope.Extension:
                {
                    var bare = parts.Extension.TrimStart('.');
                    var result = transform(bare) ?? string.Empty;
                    return parts with { Extension = result.Length == 0 ? string.Empty : "." + result.TrimStart('.') };
                }

            case RenameScope.FullName:
                {
                    var result = transform(parts.FullName) ?? string.Empty;
                    return NameParts.Split(result, context.IsDirectory);
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown rename scope.");
        }
    }
}
