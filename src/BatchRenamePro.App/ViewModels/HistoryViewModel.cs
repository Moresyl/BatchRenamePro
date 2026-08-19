using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using BatchRenamePro.App.Localization;
using BatchRenamePro.App.Services;
using BatchRenamePro.Core.Execution;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BatchRenamePro.App.ViewModels;

/// <summary>One completed batch as the history list shows it.</summary>
public sealed class HistoryEntry : ObservableObject
{
    private readonly ILocalizer _localizer;

    /// <summary>Wraps a transaction.</summary>
    /// <param name="transaction">The completed batch.</param>
    /// <param name="localizer">Translates the summary line.</param>
    public HistoryEntry(RenameTransaction transaction, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        Transaction = transaction;
        _localizer = localizer;
    }

    /// <summary>The batch itself.</summary>
    public RenameTransaction Transaction { get; }

    /// <summary>When the batch finished.</summary>
    public DateTimeOffset CompletedAt => Transaction.CompletedAt;

    /// <summary>How many items it renamed.</summary>
    public int Count => Transaction.Count;

    /// <summary>The heading: what the batch did.</summary>
    public string Title => Transaction.Label.Length > 0
        ? Transaction.Label
        : _localizer.Format("history.itemCount", Transaction.Count);

    /// <summary>The folder the batch worked in, or a count when it spanned several.</summary>
    public string Location
    {
        get
        {
            var folders = Transaction.Operations
                .Select(operation => Path.GetDirectoryName(operation.SourcePath) ?? string.Empty)
                .Where(folder => folder.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToArray();

            return folders.Length switch
            {
                0 => string.Empty,
                1 => folders[0],
                _ => _localizer["history.multipleFolders"]
            };
        }
    }

    /// <summary>The first few renames, as a preview of what undoing would put back.</summary>
    public string Sample => string.Join(
        "\n",
        Transaction.Operations
            .Take(3)
            .Select(operation => $"{Path.GetFileName(operation.SourcePath)}  →  {Path.GetFileName(operation.TargetPath)}"));

    /// <summary>Re-reads the translated text after a language change.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>
/// The history page: every batch this machine has run, newest first, each one still undoable.
/// </summary>
/// <remarks>
/// History outlives the process. A rename you regret the next morning is exactly the case a
/// session-scoped undo stack cannot serve, so the entries are read back from disk on every visit.
/// </remarks>
public sealed partial class HistoryViewModel : ObservableObject, IDisposable
{
    private readonly IHistoryStore _history;
    private readonly IRenameExecutor _executor;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly ISettingsService _settings;
    private readonly ILocalizer _localizer;
    private readonly ILogger<HistoryViewModel> _logger;

    private bool _disposed;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private HistoryEntry? _selected;

    /// <summary>Creates the page.</summary>
    /// <param name="history">Where completed runs are stored.</param>
    /// <param name="executor">Reverses a batch.</param>
    /// <param name="dialogs">Asks before undoing.</param>
    /// <param name="notifications">Reports what happened.</param>
    /// <param name="settings">Supplies the history limit.</param>
    /// <param name="localizer">Translates messages.</param>
    /// <param name="logger">Diagnostics.</param>
    public HistoryViewModel(
        IHistoryStore history,
        IRenameExecutor executor,
        IDialogService dialogs,
        INotificationService notifications,
        ISettingsService settings,
        ILocalizer localizer,
        ILogger<HistoryViewModel> logger)
    {
        _history = history;
        _executor = executor;
        _dialogs = dialogs;
        _notifications = notifications;
        _settings = settings;
        _localizer = localizer;
        _logger = logger;

        _localizer.PropertyChanged += OnLanguageChanged;
    }

    /// <summary>Raised after an undo, so the rename page can rebuild its preview.</summary>
    public event EventHandler? Reverted;

    /// <summary>The batches on record, newest first.</summary>
    public ObservableCollection<HistoryEntry> Entries { get; } = [];

    /// <summary>Whether there is anything to show.</summary>
    public bool IsEmpty => Entries.Count == 0 && !IsLoading;

    /// <summary>Re-reads the history from disk.</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;

        try
        {
            var transactions = await _history.LoadRecentAsync(_settings.Current.HistoryLimit).ConfigureAwait(true);

            Entries.Clear();
            foreach (var transaction in transactions.OrderByDescending(item => item.CompletedAt))
                Entries.Add(new HistoryEntry(transaction, _localizer));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(error, "Could not read the history");
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>Puts one batch back the way it was.</summary>
    /// <param name="entry">The batch to reverse.</param>
    [RelayCommand]
    private async Task UndoAsync(HistoryEntry? entry)
    {
        if (entry is null) return;

        var confirmed = await _dialogs.ConfirmAsync(
            _localizer["history.undo"],
            _localizer.Format("history.undoConfirm", entry.Count),
            "history.undo").ConfigureAwait(true);

        if (!confirmed) return;

        try
        {
            var reverted = await _executor.RevertAsync(entry.Transaction).ConfigureAwait(true);
            await _history.DeleteAsync(entry.Transaction.Id).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);

            _notifications.Show(_localizer.Format("run.undone", reverted.Count), NotificationKind.Success);
            Reverted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error) when (error is RenameFailedException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(error, "Could not undo {Transaction}", entry.Transaction.Id);

            await _dialogs
                .NotifyAsync(_localizer["run.failed"], _localizer["history.undoFailed"], DialogKind.Error)
                .ConfigureAwait(true);
        }
    }

    /// <summary>Removes one entry from the record without touching any files.</summary>
    /// <param name="entry">The entry to forget.</param>
    [RelayCommand]
    private async Task ForgetAsync(HistoryEntry? entry)
    {
        if (entry is null) return;

        try
        {
            await _history.DeleteAsync(entry.Transaction.Id).ConfigureAwait(true);
            Entries.Remove(entry);
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(error, "Could not forget {Transaction}", entry.Transaction.Id);
        }
    }

    /// <summary>Empties the whole record. The files themselves are left alone.</summary>
    [RelayCommand]
    private async Task ClearAsync()
    {
        if (Entries.Count == 0) return;

        var confirmed = await _dialogs.ConfirmAsync(
            _localizer["history.clear"],
            _localizer["history.clearConfirm"],
            "history.clear",
            destructive: true).ConfigureAwait(true);

        if (!confirmed) return;

        try
        {
            // Pruning to zero rather than deleting one at a time: the store owns the file layout.
            await _history.PruneAsync(0).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(error, "Could not clear the history");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _localizer.PropertyChanged -= OnLanguageChanged;
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var entry in Entries) entry.Refresh();
    }
}
