using System.Globalization;
using BatchRenamePro.Core.Planning;

namespace BatchRenamePro.Core.Execution;

/// <summary>
/// Applies a <see cref="RenamePlan"/> to disk as an all-or-nothing operation.
/// </summary>
/// <remarks>
/// <para>
/// A bulk rename is not a list of independent moves: <c>a→b, b→a</c> must swap, and <c>a→b, b→c</c>
/// must run in the right order. The executor solves both by staging — moving an item to a temporary
/// name first — but only for the items that actually need it: an item is staged when some other item
/// wants the name it currently holds. Everything else moves once. On a batch where nothing collides
/// that halves the number of file system calls compared with staging everything.
/// </para>
/// <para>
/// Every completed move is recorded before the next one starts, so a failure — or a cancellation —
/// can be unwound in reverse. If the unwind itself fails the engine reports exactly which items were
/// left behind rather than pretending the batch was clean.
/// </para>
/// </remarks>
public interface IRenameExecutor
{
    /// <summary>Renames everything the plan marked as ready.</summary>
    /// <param name="plan">The previewed plan.</param>
    /// <param name="progress">Receives an update after each completed item.</param>
    /// <param name="cancellationToken">Cancels the batch; anything already done is rolled back.</param>
    /// <exception cref="RenameFailedException">The batch could not be completed.</exception>
    Task<RenameTransaction> ExecuteAsync(
        RenamePlan plan,
        IProgress<RenameProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Puts a completed transaction back the way it was.</summary>
    /// <param name="transaction">The transaction to reverse.</param>
    /// <param name="progress">Receives an update after each completed item.</param>
    /// <param name="cancellationToken">Cancels the undo; anything already done is rolled back.</param>
    /// <exception cref="RenameFailedException">The undo could not be completed.</exception>
    Task<RenameTransaction> RevertAsync(
        RenameTransaction transaction,
        IProgress<RenameProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IRenameExecutor" />
public sealed class RenameExecutor : IRenameExecutor
{
    private const string StagingPrefix = ".~batchrename-";

    /// <inheritdoc />
    public Task<RenameTransaction> ExecuteAsync(
        RenamePlan plan,
        IProgress<RenameProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.ProblemCount > 0 || plan.HasBlockingDiagnostic)
            throw new InvalidOperationException("The plan still contains unresolved problems and cannot be executed.");

        var operations = plan.Items
            .Where(item => item.CanExecute)
            .Select(item => new RenameOperation(item.SourcePath, item.TargetPath, item.IsDirectory))
            .ToArray();

        return Task.Run(() => Run(operations, BuildLabel(operations), progress, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public Task<RenameTransaction> RevertAsync(
        RenameTransaction transaction,
        IProgress<RenameProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var operations = transaction.Operations
            .Reverse()
            .Select(operation => new RenameOperation(operation.TargetPath, operation.SourcePath, operation.IsDirectory))
            .ToArray();

        return Task.Run(() => Run(operations, "undo", progress, cancellationToken), cancellationToken);
    }

    private static RenameTransaction Run(
        RenameOperation[] operations,
        string label,
        IProgress<RenameProgress>? progress,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        if (operations.Length == 0)
            return new RenameTransaction(NewId(startedAt), startedAt, DateTimeOffset.Now, [], label);

        var sources = Validate(operations);

        // An item must get out of the way first when someone else is coming for the name it holds.
        var contested = operations
            .Select(operation => operation.TargetPath)
            .Where(sources.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var currentPaths = new Dictionary<string, string>(operations.Length, StringComparer.OrdinalIgnoreCase);
        var completed = new List<(string From, string To, bool IsDirectory)>(operations.Length * 2);

        try
        {
            foreach (var operation in operations)
            {
                if (!contested.Contains(operation.SourcePath)) continue;

                cancellationToken.ThrowIfCancellationRequested();
                var staged = CreateStagingPath(operation.SourcePath);
                Move(operation.SourcePath, staged, operation.IsDirectory);
                completed.Add((operation.SourcePath, staged, operation.IsDirectory));
                currentPaths[operation.SourcePath] = staged;
            }

            for (var i = 0; i < operations.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var operation = operations[i];
                var from = currentPaths.GetValueOrDefault(operation.SourcePath, operation.SourcePath);
                Move(from, operation.TargetPath, operation.IsDirectory);
                completed.Add((from, operation.TargetPath, operation.IsDirectory));

                progress?.Report(new RenameProgress(i + 1, operations.Length, Path.GetFileName(operation.TargetPath)));
            }
        }
        catch (OperationCanceledException)
        {
            var unrecovered = Unwind(completed);
            throw new RenameFailedException(
                "The rename was cancelled and the original names have been restored.",
                null,
                unrecovered.Count == 0,
                unrecovered);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            var unrecovered = Unwind(completed);
            throw new RenameFailedException(
                unrecovered.Count == 0
                    ? $"The rename failed and every original name has been restored: {error.Message}"
                    : $"The rename failed and {unrecovered.Count} item(s) could not be restored: {error.Message}",
                error,
                unrecovered.Count == 0,
                unrecovered);
        }

        return new RenameTransaction(NewId(startedAt), startedAt, DateTimeOffset.Now, operations, label);
    }

    /// <summary>Confirms the batch is internally consistent and nothing is standing in its way.</summary>
    private static HashSet<string> Validate(RenameOperation[] operations)
    {
        var sources = new HashSet<string>(operations.Length, StringComparer.OrdinalIgnoreCase);
        var targets = new HashSet<string>(operations.Length, StringComparer.OrdinalIgnoreCase);

        foreach (var operation in operations)
        {
            if (!File.Exists(operation.SourcePath) && !Directory.Exists(operation.SourcePath))
                throw new RenameFailedException($"The item no longer exists: {operation.SourcePath}");

            if (!sources.Add(operation.SourcePath))
                throw new RenameFailedException($"The same item appears twice in the batch: {operation.SourcePath}");

            if (!targets.Add(operation.TargetPath))
                throw new RenameFailedException($"Two items would be renamed to the same path: {operation.TargetPath}");
        }

        foreach (var operation in operations)
        {
            // Occupied by something outside the batch means it is not going to move out of the way.
            var occupied = File.Exists(operation.TargetPath) || Directory.Exists(operation.TargetPath);
            if (occupied && !sources.Contains(operation.TargetPath))
                throw new RenameFailedException($"The name is already taken: {Path.GetFileName(operation.TargetPath)}");
        }

        return sources;
    }

    /// <summary>Reverses completed moves, newest first, and reports whatever could not be put back.</summary>
    private static List<RenameOperation> Unwind(List<(string From, string To, bool IsDirectory)> completed)
    {
        var unrecovered = new List<RenameOperation>();

        for (var i = completed.Count - 1; i >= 0; i--)
        {
            var (from, to, isDirectory) = completed[i];

            try
            {
                var present = isDirectory ? Directory.Exists(to) : File.Exists(to);
                var originalFree = !File.Exists(from) && !Directory.Exists(from);

                if (present && originalFree) Move(to, from, isDirectory);
                else if (present) unrecovered.Add(new RenameOperation(from, to, isDirectory));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                unrecovered.Add(new RenameOperation(from, to, isDirectory));
            }
        }

        return unrecovered;
    }

    private static void Move(string from, string to, bool isDirectory)
    {
        if (isDirectory) Directory.Move(from, to);
        else File.Move(from, to);
    }

    private static string CreateStagingPath(string sourcePath)
    {
        var directory = Path.GetDirectoryName(sourcePath)!;
        string path;

        do
        {
            path = Path.Combine(directory, $"{StagingPrefix}{Guid.NewGuid():N}.tmp");
        }
        while (File.Exists(path) || Directory.Exists(path));

        return path;
    }

    private static string NewId(DateTimeOffset startedAt) =>
        string.Create(CultureInfo.InvariantCulture, $"{startedAt:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");

    private static string BuildLabel(RenameOperation[] operations)
    {
        if (operations.Length == 0) return string.Empty;

        var first = Path.GetFileName(operations[0].TargetPath);
        return operations.Length == 1
            ? first
            : string.Create(CultureInfo.InvariantCulture, $"{first} +{operations.Length - 1}");
    }
}
