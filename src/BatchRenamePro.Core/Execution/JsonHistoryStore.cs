using System.Text.Json;

namespace BatchRenamePro.Core.Execution;

/// <summary>Persists completed batches so they can be undone after the app is closed and reopened.</summary>
public interface IHistoryStore
{
    /// <summary>Writes a transaction and returns the file it was written to.</summary>
    Task<string> SaveAsync(RenameTransaction transaction, CancellationToken cancellationToken = default);

    /// <summary>Reads the most recent transactions, newest first.</summary>
    Task<IReadOnlyList<RenameTransaction>> LoadRecentAsync(int maximum = 50, CancellationToken cancellationToken = default);

    /// <summary>Removes one transaction from the history.</summary>
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Deletes everything except the newest <paramref name="keep"/> transactions.</summary>
    Task PruneAsync(int keep, CancellationToken cancellationToken = default);
}

/// <summary>
/// A history store backed by one JSON file per transaction under a local application data folder.
/// </summary>
/// <remarks>
/// One file per transaction rather than a single rolling log: a batch is written once and read
/// rarely, pruning is a file delete, and a partially written file can only ever cost the newest
/// entry instead of the whole history. Nothing here leaves the machine.
/// </remarks>
public sealed class JsonHistoryStore : IHistoryStore
{
    private const string FileExtension = ".json";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _directoryPath;

    /// <summary>Creates a store rooted at <paramref name="directoryPath"/>.</summary>
    public JsonHistoryStore(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        _directoryPath = directoryPath;
    }

    /// <summary>The default location: <c>%LOCALAPPDATA%\BatchRenamePro\History</c>.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BatchRenamePro",
        "History");

    /// <inheritdoc />
    public async Task<string> SaveAsync(RenameTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        Directory.CreateDirectory(_directoryPath);
        var path = Path.Combine(_directoryPath, Sanitize(transaction.Id) + FileExtension);

        // Write beside the target and swap in, so a crash mid-write cannot leave a truncated entry.
        var temporaryPath = path + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, transaction, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, path, overwrite: true);
        return path;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RenameTransaction>> LoadRecentAsync(int maximum = 50, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_directoryPath)) return [];

        var files = NewestFirst().Take(Math.Max(0, maximum));

        var transactions = new List<RenameTransaction>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var stream = file.OpenRead();
                if (await JsonSerializer.DeserializeAsync<RenameTransaction>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false) is { } transaction)
                    transactions.Add(transaction);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                // One unreadable entry must not hide the rest of the history.
            }
        }

        return transactions;
    }

    /// <inheritdoc />
    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        cancellationToken.ThrowIfCancellationRequested();

        var path = Path.Combine(_directoryPath, Sanitize(id) + FileExtension);

        try
        {
            File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Nothing to do: the entry is gone or locked, and neither is worth failing over.
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PruneAsync(int keep, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(keep);
        if (!Directory.Exists(_directoryPath)) return Task.CompletedTask;

        try
        {
            foreach (var file in NewestFirst().Skip(keep).ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    file.Delete();
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // Skip locked entries and carry on pruning.
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The history folder became unreadable; pruning is best effort.
        }

        return Task.CompletedTask;
    }

    /// <summary>The history in the order it is presented and pruned: newest first.</summary>
    /// <remarks>
    /// The name breaks ties, and it has to. Several batches saved in quick succession share a
    /// last-write time — the clock behind it only advances every 15 milliseconds or so — and with
    /// nothing to separate them the sort falls back on directory order, which is neither
    /// chronological nor stable. Ids begin <c>yyyyMMdd-HHmmss</c>, so name-descending resolves a
    /// tied group correctly down to the second; inside one second the random suffix decides, which
    /// is arbitrary but at least gives the same answer every time the folder is read.
    /// </remarks>
    private IOrderedEnumerable<FileInfo> NewestFirst() => new DirectoryInfo(_directoryPath)
        .EnumerateFiles("*" + FileExtension)
        .OrderByDescending(file => file.LastWriteTimeUtc)
        .ThenByDescending(file => file.Name, StringComparer.Ordinal);

    private static string Sanitize(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string([.. id.Select(character => Array.IndexOf(invalid, character) < 0 ? character : '_')]);
    }
}
