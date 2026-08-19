using BatchRenamePro.Core.Abstractions;

namespace BatchRenamePro.Core.Planning;

/// <summary>The outcome of evaluating the rule pipeline against one item.</summary>
public enum PlanItemStatus
{
    /// <summary>The item will be renamed.</summary>
    Ready,

    /// <summary>The rules produced the name the item already has.</summary>
    Unchanged,

    /// <summary>The target name is taken, by another item in the batch or by a file on disk.</summary>
    Conflict,

    /// <summary>The rules produced a name Windows will not accept.</summary>
    Invalid,

    /// <summary>The item disappeared from disk between selection and preview.</summary>
    Missing,

    /// <summary>Left alone on purpose, because the conflict policy is set to skip.</summary>
    Skipped
}

/// <summary>One row of the preview: where an item is now and where it is going.</summary>
/// <param name="SourcePath">Full path as it exists on disk.</param>
/// <param name="TargetPath">Full path after renaming.</param>
/// <param name="OriginalName">Current name, without directory.</param>
/// <param name="ProposedName">Name the pipeline produced.</param>
/// <param name="IsDirectory">Whether the item is a directory.</param>
/// <param name="Status">What will happen to this item.</param>
/// <param name="StatusCode">Stable identifier the presentation layer localizes.</param>
/// <param name="StatusMessage">English fallback message.</param>
/// <param name="WasAutoResolved">Whether a numeric suffix was appended to dodge a collision.</param>
public sealed record RenamePlanItem(
    string SourcePath,
    string TargetPath,
    string OriginalName,
    string ProposedName,
    bool IsDirectory,
    PlanItemStatus Status,
    string StatusCode,
    string StatusMessage,
    bool WasAutoResolved = false)
{
    /// <summary>Whether the executor will act on this item.</summary>
    public bool CanExecute => Status is PlanItemStatus.Ready;

    /// <summary>The directory containing the item.</summary>
    public string DirectoryPath => Path.GetDirectoryName(SourcePath) ?? string.Empty;
}

/// <summary>A complete, immutable preview of what a rename would do.</summary>
/// <remarks>
/// A plan is the single artifact the UI renders, the confirmation dialog summarizes and the executor
/// consumes. Nothing touches the file system until <see cref="Execution.RenameExecutor"/> is handed
/// one of these, which is what makes the preview trustworthy.
/// </remarks>
public sealed class RenamePlan
{
    /// <summary>An empty plan, used before anything is selected.</summary>
    public static RenamePlan Empty { get; } = new([], []);

    /// <summary>Creates a plan.</summary>
    /// <param name="items">One entry per selected item, in display order.</param>
    /// <param name="diagnostics">Problems with the rules themselves, independent of any item.</param>
    public RenamePlan(IReadOnlyList<RenamePlanItem> items, IReadOnlyList<RuleDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(diagnostics);

        Items = items;
        Diagnostics = diagnostics;

        foreach (var item in items)
        {
            switch (item.Status)
            {
                case PlanItemStatus.Ready: ReadyCount++; break;
                case PlanItemStatus.Unchanged: UnchangedCount++; break;
                case PlanItemStatus.Conflict: ConflictCount++; break;
                case PlanItemStatus.Invalid: InvalidCount++; break;
                case PlanItemStatus.Missing: MissingCount++; break;
                case PlanItemStatus.Skipped: SkippedCount++; break;
            }

            if (item.WasAutoResolved) AutoResolvedCount++;
        }

        HasBlockingDiagnostic = diagnostics.Any(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error);
    }

    /// <summary>One entry per selected item, in display order.</summary>
    public IReadOnlyList<RenamePlanItem> Items { get; }

    /// <summary>Problems with the rules themselves, independent of any particular item.</summary>
    public IReadOnlyList<RuleDiagnostic> Diagnostics { get; }

    /// <summary>Number of items that will be renamed.</summary>
    public int ReadyCount { get; }

    /// <summary>Number of items whose name the rules did not change.</summary>
    public int UnchangedCount { get; }

    /// <summary>Number of items blocked by a name collision.</summary>
    public int ConflictCount { get; }

    /// <summary>Number of items whose proposed name Windows rejects.</summary>
    public int InvalidCount { get; }

    /// <summary>Number of items that vanished from disk.</summary>
    public int MissingCount { get; }

    /// <summary>Number of items deliberately left alone.</summary>
    public int SkippedCount { get; }

    /// <summary>Number of items given a numeric suffix to dodge a collision.</summary>
    public int AutoResolvedCount { get; }

    /// <summary>Whether any rule is misconfigured badly enough to block the run.</summary>
    public bool HasBlockingDiagnostic { get; }

    /// <summary>Number of problems the user has to resolve before the batch can run.</summary>
    public int ProblemCount => ConflictCount + InvalidCount + MissingCount;

    /// <summary>
    /// Whether the batch can run: at least one item to rename, nothing broken, and no misconfigured rule.
    /// </summary>
    public bool CanExecute => ReadyCount > 0 && ProblemCount == 0 && !HasBlockingDiagnostic;
}
