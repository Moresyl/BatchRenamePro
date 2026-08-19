namespace BatchRenamePro.Core.Execution;

/// <summary>A single completed rename, recorded so it can be undone.</summary>
/// <param name="SourcePath">Where the item was.</param>
/// <param name="TargetPath">Where it ended up.</param>
/// <param name="IsDirectory">Whether the item is a directory.</param>
public sealed record RenameOperation(string SourcePath, string TargetPath, bool IsDirectory);

/// <summary>An executed batch, and the unit of undo.</summary>
/// <param name="Id">Identifier used for the history file name.</param>
/// <param name="StartedAt">When execution began.</param>
/// <param name="CompletedAt">When execution finished.</param>
/// <param name="Operations">Every rename that succeeded, in the order it was applied.</param>
/// <param name="Label">Short human-readable summary shown in the history list.</param>
public sealed record RenameTransaction(
    string Id,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<RenameOperation> Operations,
    string Label = "")
{
    /// <summary>Number of items renamed.</summary>
    public int Count => Operations.Count;

    /// <summary>How long the batch took.</summary>
    public TimeSpan Duration => CompletedAt - StartedAt;

    /// <summary>An empty transaction, used when a run had nothing to do.</summary>
    public static RenameTransaction Empty { get; } = new(string.Empty, default, default, []);
}

/// <summary>Progress for a running batch.</summary>
/// <param name="Completed">Items finished so far.</param>
/// <param name="Total">Items in the batch.</param>
/// <param name="CurrentName">The name being written, for display.</param>
public readonly record struct RenameProgress(int Completed, int Total, string CurrentName)
{
    /// <summary>Completion as a fraction between 0 and 1.</summary>
    public double Fraction => Total <= 0 ? 0 : Math.Clamp((double)Completed / Total, 0, 1);
}

/// <summary>Raised when a batch fails and the engine has finished putting things back.</summary>
public sealed class RenameFailedException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The underlying file system failure.</param>
    /// <param name="rolledBack">Whether every completed rename was successfully reversed.</param>
    /// <param name="unrecovered">Items the engine could not put back, if any.</param>
    public RenameFailedException(
        string message,
        Exception? innerException = null,
        bool rolledBack = true,
        IReadOnlyList<RenameOperation>? unrecovered = null)
        : base(message, innerException)
    {
        RolledBack = rolledBack;
        Unrecovered = unrecovered ?? [];
    }

    /// <summary>Creates the exception.</summary>
    /// <remarks>
    /// <see cref="RolledBack"/> is <see langword="true"/> here and on the message-only overload:
    /// these are the pre-flight rejections, thrown before anything on disk has been touched.
    /// Reporting them as <see langword="false"/> would send the user hunting for half-renamed
    /// files that do not exist.
    /// </remarks>
    public RenameFailedException()
    {
        RolledBack = true;
        Unrecovered = [];
    }

    /// <inheritdoc cref="RenameFailedException()" />
    /// <param name="message">What went wrong.</param>
    public RenameFailedException(string message) : base(message)
    {
        RolledBack = true;
        Unrecovered = [];
    }

    /// <summary>Whether the file system is back the way it was before the batch started.</summary>
    public bool RolledBack { get; }

    /// <summary>Items left in an intermediate state, which the user must resolve by hand.</summary>
    public IReadOnlyList<RenameOperation> Unrecovered { get; }
}
