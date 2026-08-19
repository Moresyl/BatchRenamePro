using BatchRenamePro.Core.Abstractions;

namespace BatchRenamePro.Core.Planning;

/// <summary>What to do when a proposed name is already taken.</summary>
/// <remarks>
/// There is deliberately no "overwrite" option. Overwriting during a bulk rename destroys the
/// target's contents with no undo path, and the two safe outcomes — pick another name, or leave the
/// item alone — cover the real use cases.
/// </remarks>
public enum ConflictPolicy
{
    /// <summary>Flag the collision and refuse to run until the user resolves it.</summary>
    Block,

    /// <summary>Append <c> (2)</c>, <c> (3)</c> and so on until the name is free.</summary>
    AutoNumber,

    /// <summary>Leave the colliding item untouched and rename the rest.</summary>
    Skip
}

/// <summary>Settings that shape a plan but are not part of any individual rule.</summary>
/// <param name="ConflictPolicy">What to do when a proposed name is taken.</param>
/// <param name="Timestamp">
/// The batch timestamp seen by time tokens. Supplied by tests for determinism; production passes
/// <see langword="null"/> to use the current time.
/// </param>
public readonly record struct PlanOptions(ConflictPolicy ConflictPolicy = ConflictPolicy.Block, DateTimeOffset? Timestamp = null)
{
    /// <summary>Default settings: collisions block the run and time tokens read the clock.</summary>
    public static PlanOptions Default { get; } = new(ConflictPolicy.Block, Timestamp: null);
}

/// <summary>An item to be renamed, as the caller already knows it.</summary>
/// <param name="Path">Full path on disk.</param>
/// <param name="IsDirectory">Whether the item is a directory.</param>
/// <param name="Facts">
/// Metadata captured when the item was added. Supplying it keeps time and size tokens off the
/// file system during preview; pass <see langword="null"/> to have it read lazily.
/// </param>
public sealed record RenameSource(string Path, bool IsDirectory, FileFacts? Facts = null)
{
    /// <summary>Inspects a path and captures what the planner needs.</summary>
    public static RenameSource FromPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var full = System.IO.Path.GetFullPath(path);
        var isDirectory = Directory.Exists(full);
        return new RenameSource(full, isDirectory, FileFacts.Read(full, isDirectory));
    }
}

/// <summary>
/// Runs the rule pipeline over a selection and produces the plan the UI previews and the executor
/// consumes.
/// </summary>
/// <remarks>
/// The planner never writes to disk. It reads only to answer "does this name already exist", and it
/// does that through a <see cref="DirectoryIndex"/> so a live preview stays responsive on large
/// folders.
/// </remarks>
public interface IRenamePlanner
{
    /// <summary>Evaluates <paramref name="rules"/> against <paramref name="sources"/>.</summary>
    /// <param name="sources">The selected items, in the order the user arranged them.</param>
    /// <param name="rules">The pipeline. Disabled rules are skipped.</param>
    /// <param name="options">Conflict policy and batch timestamp.</param>
    /// <param name="index">A reusable directory listing cache; a private one is used when omitted.</param>
    /// <param name="cancellationToken">Cancels a long preview.</param>
    RenamePlan Build(
        IReadOnlyList<RenameSource> sources,
        IReadOnlyList<IRenameRule> rules,
        PlanOptions options = default,
        DirectoryIndex? index = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IRenamePlanner" />
public sealed class RenamePlanner : IRenamePlanner
{
    /// <inheritdoc />
    public RenamePlan Build(
        IReadOnlyList<RenameSource> sources,
        IReadOnlyList<IRenameRule> rules,
        PlanOptions options = default,
        DirectoryIndex? index = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(rules);

        var active = rules.Where(rule => rule.IsEnabled).ToArray();
        var diagnostics = active.SelectMany(rule => rule.Validate()).ToArray();
        var blocked = diagnostics.Any(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error);

        if (sources.Count == 0) return new RenamePlan([], diagnostics);

        var timestamp = options.Timestamp ?? DateTimeOffset.Now;
        var directoryIndex = index ?? new DirectoryIndex();
        var items = new List<RenamePlanItem>(sources.Count);

        for (var i = 0; i < sources.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(Evaluate(sources[i], i, sources.Count, active, blocked, timestamp));
        }

        ResolveCollisions(items, options.ConflictPolicy, directoryIndex, cancellationToken);
        return new RenamePlan(items, diagnostics);
    }

    /// <summary>
    /// Runs the pipeline for one item and classifies the result, without looking at any other item.
    /// Collisions are settled afterwards, once every proposed name is known.
    /// </summary>
    private static RenamePlanItem Evaluate(
        RenameSource source,
        int index,
        int total,
        IReadOnlyList<IRenameRule> rules,
        bool rulesBlocked,
        DateTimeOffset timestamp)
    {
        var originalName = Path.GetFileName(source.Path);
        var directory = Path.GetDirectoryName(source.Path) ?? string.Empty;

        if (!File.Exists(source.Path) && !Directory.Exists(source.Path))
        {
            return Stationary(source, originalName, PlanItemStatus.Missing, "item.missing", "The item no longer exists on disk.");
        }

        if (rulesBlocked)
        {
            return Stationary(source, originalName, PlanItemStatus.Invalid, "item.ruleError", "Fix the highlighted rule before running.");
        }

        var context = new RenameContext(source.Path, source.IsDirectory, index, total, timestamp, source.Facts);
        var parts = NameParts.Split(originalName, source.IsDirectory);

        foreach (var rule in rules)
        {
            // A misbehaving rule should cost one row of the preview, not the whole preview.
            try
            {
                parts = rule.Apply(parts, context);
            }
            catch (Exception error) when (error is ArgumentException or FormatException or OverflowException or IndexOutOfRangeException)
            {
                return Stationary(source, originalName, PlanItemStatus.Invalid, "item.ruleFailed", $"A rule could not be applied: {error.Message}");
            }
        }

        var proposedName = parts.FullName;

        if (FileNameValidator.Validate(proposedName) is { } problem)
        {
            return Stationary(source, proposedName, PlanItemStatus.Invalid, problem.Code, problem.Message);
        }

        var targetPath = Path.Combine(directory, proposedName);

        // Ordinal on purpose: changing only the casing is a real rename, not a no-op.
        if (string.Equals(source.Path, targetPath, StringComparison.Ordinal))
        {
            return new RenamePlanItem(
                source.Path, targetPath, originalName, proposedName, source.IsDirectory,
                PlanItemStatus.Unchanged, "item.unchanged", "The rules did not change this name.");
        }

        return new RenamePlanItem(
            source.Path, targetPath, originalName, proposedName, source.IsDirectory,
            PlanItemStatus.Ready, "item.ready", "Ready to rename.");

        static RenamePlanItem Stationary(RenameSource source, string proposedName, PlanItemStatus status, string code, string message) =>
            new(source.Path, source.Path, Path.GetFileName(source.Path), proposedName, source.IsDirectory, status, code, message);
    }

    private static void ResolveCollisions(
        List<RenamePlanItem> items,
        ConflictPolicy policy,
        DirectoryIndex directoryIndex,
        CancellationToken cancellationToken)
    {
        if (policy is ConflictPolicy.AutoNumber)
        {
            AutoNumber(items, directoryIndex);
            return;
        }

        // Demoting one item can free a name or, more often, block another: if A wants B's name and B
        // turns out to be blocked, B never moves and A is blocked too. Each pass demotes at least one
        // item, so iterating until nothing changes converges in at most one pass per item.
        for (var pass = 0; pass <= items.Count; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var vacating = items
                .Where(item => item.Status is PlanItemStatus.Ready)
                .Select(item => item.SourcePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var duplicateTargets = items
                .Where(item => item.Status is PlanItemStatus.Ready)
                .GroupBy(item => item.TargetPath, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var changed = false;

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Status is not PlanItemStatus.Ready) continue;

                if (duplicateTargets.Contains(item.TargetPath))
                {
                    items[i] = Demote(item, policy, "item.duplicateTarget", "Several items would end up with this name.");
                    changed = true;
                    continue;
                }

                var occupied = directoryIndex.Contains(item.DirectoryPath, item.ProposedName) && !vacating.Contains(item.TargetPath);
                if (occupied)
                {
                    items[i] = Demote(item, policy, "item.existsOnDisk", "An item with that name already exists here.");
                    changed = true;
                }
            }

            if (!changed) return;
        }
    }

    private static RenamePlanItem Demote(RenamePlanItem item, ConflictPolicy policy, string code, string message) =>
        policy is ConflictPolicy.Skip
            ? item with
            {
                TargetPath = item.SourcePath,
                Status = PlanItemStatus.Skipped,
                StatusCode = "item.skipped",
                StatusMessage = "Skipped: the name is already taken."
            }
            : item with { Status = PlanItemStatus.Conflict, StatusCode = code, StatusMessage = message };

    /// <summary>Appends " (2)", " (3)" and so on until each proposed name is unique and free.</summary>
    private static void AutoNumber(List<RenamePlanItem> items, DirectoryIndex directoryIndex)
    {
        // Items that are not moving keep hold of their current name, so nothing else may claim it.
        var claimed = items
            .Where(item => item.Status is not PlanItemStatus.Ready)
            .Select(item => item.SourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Names that a moving item is about to release are fair game for someone else.
        var vacating = items
            .Where(item => item.Status is PlanItemStatus.Ready)
            .Select(item => item.SourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.Status is not PlanItemStatus.Ready) continue;

            var directory = item.DirectoryPath;
            var parts = NameParts.Split(item.ProposedName, item.IsDirectory);
            var candidateName = item.ProposedName;
            var candidate = item.TargetPath;

            for (var suffix = 2; IsTaken(candidate, candidateName) && suffix <= 10_000; suffix++)
            {
                candidateName = $"{parts.BaseName} ({suffix}){parts.Extension}";
                candidate = Path.Combine(directory, candidateName);
            }

            claimed.Add(candidate);

            var resolved = !string.Equals(candidate, item.TargetPath, StringComparison.Ordinal);
            items[i] = item with
            {
                TargetPath = candidate,
                ProposedName = candidateName,
                StatusCode = resolved ? "item.autoResolved" : "item.ready",
                StatusMessage = resolved ? "A numeric suffix was added to avoid a collision." : "Ready to rename.",
                WasAutoResolved = resolved
            };

            bool IsTaken(string path, string name)
            {
                if (claimed.Contains(path)) return true;
                if (vacating.Contains(path)) return false;
                return directoryIndex.Contains(directory, name);
            }
        }
    }
}
