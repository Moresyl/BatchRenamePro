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
/// <para>
/// A batch may contain a folder and the items inside it at the same time. Every path in it is written
/// against the tree as it stood when the plan was made, and renaming the folder invalidates all of
/// them at once, so the executor keeps a running map of the folders it has already moved and resolves
/// each path through it. That is what lets a batch be undone: an undo replays the same moves in
/// reverse, which necessarily puts the folder back before the items inside it.
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

        var moved = new PathMap();
        var completed = new List<(string From, string To, bool IsDirectory)>(operations.Length * 2);

        try
        {
            foreach (var operation in operations)
            {
                if (!contested.Contains(operation.SourcePath)) continue;

                cancellationToken.ThrowIfCancellationRequested();
                var source = moved.Resolve(operation.SourcePath);
                var staged = CreateStagingPath(source);
                Move(source, staged, operation.IsDirectory);
                completed.Add((source, staged, operation.IsDirectory));
                moved.Record(source, staged, operation.IsDirectory);
            }

            for (var i = 0; i < operations.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var operation = operations[i];
                var from = moved.Resolve(operation.SourcePath);

                // The target is resolved through its parent only. Resolving the whole path would
                // follow a staging move made for the item that currently holds this very name, and
                // send the rename to the temporary file instead of to the name being vacated.
                var to = moved.ResolveParentOf(operation.TargetPath);

                Move(from, to, operation.IsDirectory);
                completed.Add((from, to, operation.IsDirectory));
                moved.Record(from, to, operation.IsDirectory);

                progress?.Report(new RenameProgress(i + 1, operations.Length, Path.GetFileName(to)));
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
            if (!sources.Add(operation.SourcePath))
                throw new RenameFailedException($"The same item appears twice in the batch: {operation.SourcePath}");

            if (!targets.Add(operation.TargetPath))
                throw new RenameFailedException($"Two items would be renamed to the same path: {operation.TargetPath}");
        }

        for (var i = 0; i < operations.Length; i++)
        {
            var operation = operations[i];

            // Not simply "does this exist right now": an item inside a folder that an earlier
            // operation moves is described by a path that only becomes real once that move has
            // happened. Undoing a batch that renamed a folder together with its contents is exactly
            // that case, and rejecting it here would make such a batch permanent.
            if (!File.Exists(operation.SourcePath) && !Directory.Exists(operation.SourcePath) && !Produces(operations, i, operation.SourcePath))
                throw new RenameFailedException($"The item no longer exists: {operation.SourcePath}");

            // Occupied by something outside the batch means it is not going to move out of the way.
            var occupied = File.Exists(operation.TargetPath) || Directory.Exists(operation.TargetPath);
            if (occupied && !sources.Contains(operation.TargetPath))
                throw new RenameFailedException($"The name is already taken: {Path.GetFileName(operation.TargetPath)}");
        }

        return sources;
    }

    /// <summary>Whether one of the first <paramref name="count"/> operations puts <paramref name="path"/> within reach.</summary>
    private static bool Produces(RenameOperation[] operations, int count, string path)
    {
        for (var i = 0; i < count; i++)
        {
            var target = operations[i].TargetPath;
            if (PathMap.Same(path, target)) return true;
            if (operations[i].IsDirectory && PathMap.IsUnder(path, target)) return true;
        }

        return false;
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

    /// <summary>
    /// Where the paths of a batch have gone, as the batch is carried out.
    /// </summary>
    /// <remarks>
    /// Two kinds of move are recorded and they behave differently. A file that moves only redirects
    /// its own path. A folder that moves redirects its own path and every path beneath it, which is
    /// the whole point: <c>photos\raw\a.jpg</c> is no longer anywhere once <c>raw</c> becomes
    /// <c>original</c>, and the batch still has an entry that calls it by the old name.
    /// </remarks>
    private sealed class PathMap
    {
        private readonly List<(string From, string To, bool IsDirectory)> _moves = [];

        /// <summary>Notes that <paramref name="from"/> is now at <paramref name="to"/>.</summary>
        public void Record(string from, string to, bool isDirectory)
        {
            if (Same(from, to)) return;
            _moves.Add((from, to, isDirectory));
        }

        /// <summary>Follows a path recorded before the batch started to wherever it is now.</summary>
        public string Resolve(string path)
        {
            foreach (var (from, to, isDirectory) in _moves)
            {
                if (Same(path, from)) path = to;
                else if (isDirectory && IsUnder(path, from)) path = to + path[from.Length..];
            }

            return path;
        }

        /// <summary>
        /// Follows the folder a path sits in, leaving the name itself alone. Used for the destination
        /// of a rename, which does not exist yet and so cannot have moved.
        /// </summary>
        public string ResolveParentOf(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory)) return path;

            var resolved = Resolve(directory);
            return Same(resolved, directory) ? path : Path.Combine(resolved, Path.GetFileName(path));
        }

        /// <summary>Whether two paths name the same item, by the file system's own rules.</summary>
        public static bool Same(string left, string right) =>
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        /// <summary>Whether <paramref name="path"/> sits inside <paramref name="directory"/>.</summary>
        public static bool IsUnder(string path, string directory) =>
            path.Length > directory.Length
            && path.StartsWith(directory, StringComparison.OrdinalIgnoreCase)
            && (path[directory.Length] == Path.DirectorySeparatorChar || path[directory.Length] == Path.AltDirectorySeparatorChar);
    }
}
