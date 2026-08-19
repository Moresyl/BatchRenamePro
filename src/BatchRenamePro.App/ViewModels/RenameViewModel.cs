using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Threading;
using BatchRenamePro.App.Localization;
using BatchRenamePro.App.Services;
using BatchRenamePro.Core.Abstractions;
using BatchRenamePro.Core.Execution;
using BatchRenamePro.Core.Planning;
using BatchRenamePro.Core.Presets;
using BatchRenamePro.Core.Scanning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BatchRenamePro.App.ViewModels;

/// <summary>
/// The rename page: the selected items, the rule pipeline, the live preview and the run button.
/// </summary>
/// <remarks>
/// The preview is rebuilt from scratch whenever anything changes, on a background thread and behind
/// a short debounce. Rebuilding wholesale rather than patching the affected rows keeps the preview
/// honest — one rule change can renumber every item — and the debounce is what stops a fast typist
/// from queueing one plan per keystroke over a folder of ten thousand files.
/// </remarks>
public sealed partial class RenameViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan PreviewDelay = TimeSpan.FromMilliseconds(180);

    private readonly IFileScanner _scanner;
    private readonly IRenamePlanner _planner;
    private readonly IRenameExecutor _executor;
    private readonly IPresetStore _presets;
    private readonly IHistoryStore _history;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly ISettingsService _settings;
    private readonly ILocalizer _localizer;
    private readonly ILogger<RenameViewModel> _logger;

    private readonly List<RenameSource> _sources = [];
    private readonly DispatcherTimer _debounce;

    private CancellationTokenSource? _previewCancellation;
    private CancellationTokenSource? _runCancellation;
    private RenameTransaction? _lastTransaction;
    private bool _disposed;

    [ObservableProperty]
    private RenamePlan _plan = RenamePlan.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyMessage = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string _includePatterns = string.Empty;

    [ObservableProperty]
    private string _excludePatterns = string.Empty;

    [ObservableProperty]
    private RuleCardViewModel? _selectedRule;

    /// <summary>Creates the page.</summary>
    /// <param name="scanner">Expands folders into items.</param>
    /// <param name="planner">Builds the preview.</param>
    /// <param name="executor">Performs and reverses renames.</param>
    /// <param name="presets">Where user presets live.</param>
    /// <param name="history">Where completed runs are recorded.</param>
    /// <param name="dialogs">Asks the user things.</param>
    /// <param name="notifications">Reports what happened.</param>
    /// <param name="settings">Remembered options.</param>
    /// <param name="localizer">Translates messages.</param>
    /// <param name="logger">Diagnostics.</param>
    public RenameViewModel(
        IFileScanner scanner,
        IRenamePlanner planner,
        IRenameExecutor executor,
        IPresetStore presets,
        IHistoryStore history,
        IDialogService dialogs,
        INotificationService notifications,
        ISettingsService settings,
        ILocalizer localizer,
        ILogger<RenameViewModel> logger)
    {
        _scanner = scanner;
        _planner = planner;
        _executor = executor;
        _presets = presets;
        _history = history;
        _dialogs = dialogs;
        _notifications = notifications;
        _settings = settings;
        _localizer = localizer;
        _logger = logger;

        Rules = [];
        Rules.CollectionChanged += (_, _) => SchedulePreview();

        _debounce = new DispatcherTimer { Interval = PreviewDelay };
        _debounce.Tick += OnDebounceTick;

        _localizer.PropertyChanged += OnLanguageChanged;
    }

    /// <summary>Raised after a batch completes, so the history page knows to reload.</summary>
    public event EventHandler? RunCompleted;

    /// <summary>The rule pipeline, in execution order.</summary>
    public ObservableCollection<RuleCardViewModel> Rules { get; }

    /// <summary>The preview rows currently on screen, after the "only changes" filter.</summary>
    public ObservableCollection<RenamePlanItem> Rows { get; } = [];

    /// <summary>Every preset, built-in ones first.</summary>
    public ObservableCollection<PresetItem> Presets { get; } = [];

    /// <summary>The rule types offered by the add menu.</summary>
    public static IReadOnlyList<RuleDescriptor> AvailableRules => RuleCatalog.All;

    /// <summary>How many items are selected.</summary>
    public int SourceCount => _sources.Count;

    /// <summary>Whether anything is selected at all.</summary>
    public bool HasSources => _sources.Count > 0;

    /// <summary>Whether the pipeline has any rules.</summary>
    public bool HasRules => Rules.Count > 0;

    /// <summary>Whether the run button is live.</summary>
    public bool CanRun => Plan.CanExecute && !IsBusy;

    /// <summary>Whether the last completed run can still be undone.</summary>
    public bool CanUndo => _lastTransaction is { Count: > 0 } && !IsBusy;

    /// <summary>A one-line description of what the run will do.</summary>
    public string Summary
    {
        get
        {
            if (_sources.Count == 0) return _localizer["preview.empty.hint"];

            var parts = new List<string>(4);
            if (Plan.ReadyCount > 0) parts.Add(_localizer.Format("summary.ready", Plan.ReadyCount));
            if (Plan.UnchangedCount > 0) parts.Add(_localizer.Format("summary.unchanged", Plan.UnchangedCount));
            if (Plan.ProblemCount > 0) parts.Add(_localizer.Format("summary.problems", Plan.ProblemCount));
            if (Plan.AutoResolvedCount > 0) parts.Add(_localizer.Format("summary.autoResolved", Plan.AutoResolvedCount));

            return parts.Count == 0 ? _localizer["summary.nothing"] : string.Join(" · ", parts);
        }
    }

    // ---- options, every one of them persisted --------------------------------------------------

    /// <summary>What happens when two items want the same name.</summary>
    public ConflictPolicy ConflictPolicy
    {
        get => _settings.Current.ConflictPolicy;
        set => SetOption(value, ConflictPolicy, policy => _settings.Current.ConflictPolicy = policy);
    }

    /// <summary>Whether adding a folder pulls in its subfolders.</summary>
    public bool Recursive
    {
        get => _settings.Current.RecursiveByDefault;
        set => SetOption(value, Recursive, flag => _settings.Current.RecursiveByDefault = flag, replan: false);
    }

    /// <summary>Whether adding a folder collects files, folders or both.</summary>
    public ScanTarget ScanTarget
    {
        get => _settings.Current.ScanTarget;
        set => SetOption(value, ScanTarget, target => _settings.Current.ScanTarget = target, replan: false);
    }

    /// <summary>Whether the preview hides rows the rules did not change.</summary>
    public bool ShowOnlyChanged
    {
        get => _settings.Current.ShowOnlyChanged;
        set => SetOption(value, ShowOnlyChanged, flag => _settings.Current.ShowOnlyChanged = flag);
    }

    // ---- selection ------------------------------------------------------------------------------

    /// <summary>Opens the folder picker and adds what it returns.</summary>
    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var folder = _dialogs.PickFolder(_settings.Current.LastFolder);
        if (folder is null) return;

        _settings.Current.LastFolder = folder;
        _settings.Save();

        await AddPathsAsync([folder]).ConfigureAwait(true);
    }

    /// <summary>Opens the file picker and adds what it returns.</summary>
    [RelayCommand]
    private async Task AddFilesAsync()
    {
        var files = _dialogs.PickFiles(_settings.Current.LastFolder);
        if (files.Count == 0) return;

        if (Path.GetDirectoryName(files[0]) is { Length: > 0 } folder)
        {
            _settings.Current.LastFolder = folder;
            _settings.Save();
        }

        await AddPathsAsync(files).ConfigureAwait(true);
    }

    /// <summary>Adds paths, expanding any folders. This is also what a file drop calls.</summary>
    /// <param name="paths">Files or folders, in the order the user gave them.</param>
    public async Task AddPathsAsync(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0) return;

        var options = ScanOptions.Default with
        {
            Target = ScanTarget,
            Recursive = Recursive,
            IncludePatterns = IncludePatterns,
            ExcludePatterns = ExcludePatterns
        };

        // A dropped folder of ten thousand files takes long enough that scanning on the UI thread
        // would freeze the window mid-drop.
        var found = await Task.Run(() => Collect(paths, options)).ConfigureAwait(true);

        var known = _sources.Select(source => source.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;

        foreach (var source in found)
        {
            if (!known.Add(source.Path)) continue;
            _sources.Add(source);
            added++;
        }

        if (added == 0)
        {
            _notifications.Show(_localizer["source.noneAdded"], NotificationKind.Information);
            return;
        }

        NotifySourcesChanged();
        SchedulePreview();
    }

    private List<RenameSource> Collect(IReadOnlyList<string> paths, ScanOptions options)
    {
        var found = new List<RenameSource>();

        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path)) found.AddRange(_scanner.Scan(path, options));
                else if (File.Exists(path)) found.Add(RenameSource.FromPath(path));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // One unreadable folder in a multi-folder drop must not lose the rest of the drop.
                _logger.LogWarning(error, "Skipped {Path} while collecting items", path);
            }
        }

        return found;
    }

    /// <summary>Empties the selection.</summary>
    [RelayCommand]
    private void ClearSources()
    {
        if (_sources.Count == 0) return;

        _sources.Clear();
        NotifySourcesChanged();
        SchedulePreview();
    }

    /// <summary>Drops the highlighted preview rows from the selection.</summary>
    /// <param name="selection">The rows the list has selected.</param>
    [RelayCommand]
    private void RemoveSelected(System.Collections.IList? selection)
    {
        if (selection is null || selection.Count == 0) return;

        var removing = selection
            .OfType<RenamePlanItem>()
            .Select(item => item.SourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (_sources.RemoveAll(source => removing.Contains(source.Path)) == 0) return;

        NotifySourcesChanged();
        SchedulePreview();
    }

    /// <summary>Re-reads every selected item from disk, picking up changes made outside the app.</summary>
    [RelayCommand]
    private void Refresh()
    {
        for (var index = 0; index < _sources.Count; index++)
        {
            var source = _sources[index];
            _sources[index] = source with { Facts = FileFacts.Read(source.Path, source.IsDirectory) };
        }

        SchedulePreview();
    }

    /// <summary>Opens the folder containing a preview row in File Explorer.</summary>
    /// <param name="item">The row that was double-clicked.</param>
    [RelayCommand]
    private void RevealInExplorer(RenamePlanItem? item)
    {
        if (item is null) return;

        try
        {
            // /select needs the path quoted as one argument, or a name with a space splits in two.
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{item.SourcePath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning(error, "Could not reveal {Path}", item.SourcePath);
        }
    }

    // ---- rules ----------------------------------------------------------------------------------

    /// <summary>Appends a new rule of the chosen type.</summary>
    /// <param name="descriptor">The menu entry that was clicked.</param>
    [RelayCommand]
    private void AddRule(RuleDescriptor? descriptor)
    {
        if (descriptor is null) return;

        SelectedRule = Attach(descriptor.Create());
        NotifyRuleState();
    }

    /// <summary>Deletes a rule.</summary>
    /// <param name="card">The card's view model.</param>
    [RelayCommand]
    private void RemoveRule(RuleCardViewModel? card)
    {
        if (card is null) return;

        var index = Rules.IndexOf(card);
        if (index < 0) return;

        Rules.RemoveAt(index);
        Detach(card);

        SelectedRule = Rules.Count == 0 ? null : Rules[Math.Min(index, Rules.Count - 1)];
        NotifyRuleState();
    }

    /// <summary>Adds a copy of a rule directly below it.</summary>
    /// <param name="card">The card's view model.</param>
    [RelayCommand]
    private void DuplicateRule(RuleCardViewModel? card)
    {
        if (card is null) return;

        var index = Rules.IndexOf(card);
        if (index < 0) return;

        SelectedRule = Attach(card.Rule.Clone(), index + 1);
        NotifyRuleState();
    }

    /// <summary>Moves a rule one step earlier in the pipeline.</summary>
    /// <param name="card">The card's view model.</param>
    [RelayCommand]
    private void MoveRuleUp(RuleCardViewModel? card) => Move(card, -1);

    /// <summary>Moves a rule one step later in the pipeline.</summary>
    /// <param name="card">The card's view model.</param>
    [RelayCommand]
    private void MoveRuleDown(RuleCardViewModel? card) => Move(card, +1);

    /// <summary>Deletes every rule.</summary>
    [RelayCommand]
    private void ClearRules()
    {
        if (Rules.Count == 0) return;

        foreach (var card in Rules) Detach(card);

        Rules.Clear();
        SelectedRule = null;
        NotifyRuleState();
    }

    private void Move(RuleCardViewModel? card, int offset)
    {
        if (card is null) return;

        var from = Rules.IndexOf(card);
        var to = from + offset;
        if (from < 0 || to < 0 || to >= Rules.Count) return;

        Rules.Move(from, to);
        SelectedRule = card;
    }

    private RuleCardViewModel Attach(IRenameRule rule, int? at = null)
    {
        var card = new RuleCardViewModel(rule, _localizer);
        card.Changed += OnRuleCardChanged;

        if (at is { } index && index <= Rules.Count) Rules.Insert(index, card);
        else Rules.Add(card);

        return card;
    }

    private void Detach(RuleCardViewModel card)
    {
        card.Changed -= OnRuleCardChanged;
        card.Dispose();
    }

    private void OnRuleCardChanged(object? sender, EventArgs e) => SchedulePreview();

    // ---- presets --------------------------------------------------------------------------------

    /// <summary>Loads the preset gallery. Called once when the shell starts.</summary>
    public async Task LoadPresetsAsync()
    {
        Presets.Clear();
        foreach (var preset in BuiltInPresets.All) Presets.Add(new PresetItem(preset, _localizer));

        try
        {
            foreach (var preset in await _presets.LoadAsync().ConfigureAwait(true))
                Presets.Add(new PresetItem(preset, _localizer));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(error, "Could not read the saved presets");
        }
    }

    /// <summary>Replaces the pipeline with a preset's rules.</summary>
    /// <param name="item">The preset that was clicked.</param>
    [RelayCommand]
    private void ApplyPreset(PresetItem? item)
    {
        if (item is null) return;

        ClearRules();

        // A deep copy, so editing the rules afterwards does not quietly rewrite the stored preset.
        var copy = item.Preset.DeepCopy();
        foreach (var rule in copy.Rules) Attach(rule);

        ConflictPolicy = copy.ConflictPolicy;
        SelectedRule = Rules.Count > 0 ? Rules[0] : null;

        NotifyRuleState();
        _notifications.Show(_localizer.Format("presets.applied", item.Name), NotificationKind.Success);
    }

    /// <summary>Saves the current pipeline under a name.</summary>
    [RelayCommand(CanExecute = nameof(HasRules))]
    private async Task SavePresetAsync()
    {
        if (Rules.Count == 0) return;

        var name = await _dialogs
            .PromptAsync(_localizer["presets.saveTitle"], _localizer["presets.namePrompt"])
            .ConfigureAwait(true);

        if (name is null) return;

        // Ordinal, because a preset name is an identifier for a file on disk rather than prose: two
        // names that a culture would fold together still land in two different files.
        var existing = Presets.FirstOrDefault(preset =>
            string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase));

        if (existing is { IsBuiltIn: true })
        {
            // Built-ins are the escape hatch when someone's own preset goes wrong; never shadow one.
            await _dialogs
                .NotifyAsync(_localizer["common.warning"], _localizer["presets.nameReserved"])
                .ConfigureAwait(true);

            return;
        }

        if (existing is not null)
        {
            var overwrite = await _dialogs
                .ConfirmAsync(_localizer["presets.saveTitle"], _localizer.Format("presets.overwrite", name))
                .ConfigureAwait(true);

            if (!overwrite) return;
        }

        try
        {
            await _presets.SaveAsync(new RulePreset(name, Snapshot(), ConflictPolicy)).ConfigureAwait(true);
            await LoadPresetsAsync().ConfigureAwait(true);
            _notifications.Show(_localizer.Format("presets.saved", name), NotificationKind.Success);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(error, "Could not save the preset {Preset}", name);
            await _dialogs.NotifyAsync(_localizer["common.error"], error.Message, DialogKind.Error).ConfigureAwait(true);
        }
    }

    /// <summary>Deletes a user preset.</summary>
    /// <param name="item">The preset to delete.</param>
    [RelayCommand]
    private async Task DeletePresetAsync(PresetItem? item)
    {
        if (item is null || item.IsBuiltIn) return;

        var confirmed = await _dialogs.ConfirmAsync(
            _localizer["presets.delete"],
            _localizer.Format("presets.deleteConfirm", item.Name),
            "presets.delete",
            destructive: true).ConfigureAwait(true);

        if (!confirmed) return;

        try
        {
            await _presets.DeleteAsync(item.Preset.Name).ConfigureAwait(true);
            await LoadPresetsAsync().ConfigureAwait(true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(error, "Could not delete the preset {Preset}", item.Preset.Name);
        }
    }

    /// <summary>Reads a preset from a file the user picks and adds it to the gallery.</summary>
    [RelayCommand]
    private async Task ImportPresetAsync()
    {
        var path = _dialogs.PickOpenFile(_localizer["presets.filter"]);
        if (path is null) return;

        try
        {
            var imported = await _presets.ImportAsync(path).ConfigureAwait(true);
            await _presets.SaveAsync(imported).ConfigureAwait(true);
            await LoadPresetsAsync().ConfigureAwait(true);

            _notifications.Show(_localizer.Format("presets.imported", imported.Name), NotificationKind.Success);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            _logger.LogError(error, "Could not import a preset from {Path}", path);
            await _dialogs.NotifyAsync(_localizer["common.error"], error.Message, DialogKind.Error).ConfigureAwait(true);
        }
    }

    /// <summary>Writes a preset to a file the user picks.</summary>
    /// <param name="item">The preset to export.</param>
    [RelayCommand]
    private async Task ExportPresetAsync(PresetItem? item)
    {
        if (item is null) return;

        var path = _dialogs.PickSaveFile(_localizer["presets.filter"], item.Name + ".preset.json");
        if (path is null) return;

        try
        {
            await _presets.ExportAsync(item.Preset, path).ConfigureAwait(true);
            _notifications.Show(_localizer.Format("presets.exported", item.Name), NotificationKind.Success);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(error, "Could not export the preset {Preset}", item.Preset.Name);
            await _dialogs.NotifyAsync(_localizer["common.error"], error.Message, DialogKind.Error).ConfigureAwait(true);
        }
    }

    // Presets hold RenameRuleBase rather than IRenameRule so the polymorphic JSON contract has a
    // single root to hang its $type discriminators off.
    private IReadOnlyList<RenameRuleBase> Snapshot() =>
        [.. Rules.Select(card => card.Rule.Clone()).OfType<RenameRuleBase>()];

    // ---- preview --------------------------------------------------------------------------------

    /// <summary>Queues a preview rebuild, replacing any rebuild already waiting.</summary>
    public void SchedulePreview()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private async void OnDebounceTick(object? sender, EventArgs e)
    {
        // A timer tick cannot be awaited by its caller, so an escaping exception would have nowhere
        // to surface but the finalizer. RunPreviewAsync therefore handles its own failures.
        await RunPreviewAsync().ConfigureAwait(true);
    }

    private async Task RunPreviewAsync()
    {
        _debounce.Stop();

        // The previous preview is now answering a question nobody is asking any more.
        if (_previewCancellation is { } previous)
        {
            await previous.CancelAsync().ConfigureAwait(true);
            previous.Dispose();
        }

        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;

        var sources = _sources.ToArray();
        var rules = Rules.Select(card => card.Rule).ToArray();
        var options = new PlanOptions(ConflictPolicy);

        try
        {
            var plan = await Task
                .Run(() => _planner.Build(sources, rules, options, index: null, cancellation.Token), cancellation.Token)
                .ConfigureAwait(true);

            if (cancellation.Token.IsCancellationRequested) return;

            Plan = plan;
            Fill(plan);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer preview, which will publish its own result.
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(error, "The preview could not be built");
        }
        finally
        {
            if (ReferenceEquals(_previewCancellation, cancellation))
            {
                cancellation.Dispose();
                _previewCancellation = null;
            }
        }
    }

    private void Fill(RenamePlan plan)
    {
        Rows.Clear();

        foreach (var item in plan.Items)
        {
            if (ShowOnlyChanged && item.Status is PlanItemStatus.Unchanged) continue;
            Rows.Add(item);
        }

        OnPropertyChanged(nameof(Summary));
        NotifyRunState();
    }

    // ---- running --------------------------------------------------------------------------------

    /// <summary>Applies the plan to disk.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        if (!Plan.CanExecute || IsBusy) return;

        if (_settings.Current.ConfirmBeforeRun)
        {
            var confirmed = await _dialogs.ConfirmAsync(
                _localizer["run.confirmTitle"],
                _localizer.Format("run.confirmBody", Plan.ReadyCount),
                "action.run").ConfigureAwait(true);

            if (!confirmed) return;
        }

        var plan = Plan;
        using var cancellation = new CancellationTokenSource();
        _runCancellation = cancellation;

        Begin(_localizer["run.working"]);

        try
        {
            var transaction = await _executor
                .ExecuteAsync(plan, new Progress<RenameProgress>(Report), cancellation.Token)
                .ConfigureAwait(true);

            _lastTransaction = transaction;
            await RecordAsync(transaction).ConfigureAwait(true);
            AdvanceSelection(transaction);

            _notifications.Show(
                _localizer.Format("run.done", transaction.Count),
                NotificationKind.Success,
                _localizer["action.undo"],
                () => UndoAsync(transaction));

            RunCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            _notifications.Show(_localizer["run.cancelled"], NotificationKind.Information);
        }
        catch (RenameFailedException error)
        {
            _logger.LogError(error, "The batch failed");

            var detail = error.RolledBack
                ? _localizer["run.rolledBack"]
                : _localizer.Format("run.unrecovered", error.Unrecovered.Count);

            await _dialogs
                .NotifyAsync(_localizer["run.failed"], $"{error.Message}\n\n{detail}", DialogKind.Error)
                .ConfigureAwait(true);
        }
        finally
        {
            _runCancellation = null;
            End();
        }
    }

    /// <summary>Stops a batch in flight. Everything already done is rolled back.</summary>
    [RelayCommand]
    private void CancelRun() => _runCancellation?.Cancel();

    /// <summary>Reverses the most recent run.</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoLastAsync()
    {
        if (_lastTransaction is { } transaction) await UndoAsync(transaction).ConfigureAwait(true);
    }

    private async Task UndoAsync(RenameTransaction transaction)
    {
        if (IsBusy) return;

        Begin(_localizer["action.undo"]);

        try
        {
            var reverted = await _executor
                .RevertAsync(transaction, new Progress<RenameProgress>(Report))
                .ConfigureAwait(true);

            if (ReferenceEquals(_lastTransaction, transaction)) _lastTransaction = null;

            await _history.DeleteAsync(transaction.Id).ConfigureAwait(true);
            AdvanceSelection(reverted);

            _notifications.Show(_localizer.Format("run.undone", reverted.Count), NotificationKind.Success);
            RunCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error) when (error is RenameFailedException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(error, "Could not undo {Transaction}", transaction.Id);

            await _dialogs
                .NotifyAsync(_localizer["run.failed"], _localizer["history.undoFailed"], DialogKind.Error)
                .ConfigureAwait(true);
        }
        finally
        {
            End();
        }
    }

    private void Begin(string message)
    {
        IsBusy = true;
        BusyMessage = message;
        Progress = 0;
        ProgressText = string.Empty;
    }

    private void End()
    {
        IsBusy = false;
        BusyMessage = string.Empty;
        Progress = 0;
        ProgressText = string.Empty;
        SchedulePreview();
    }

    private void Report(RenameProgress progress)
    {
        Progress = progress.Fraction;
        ProgressText = _localizer.Format("run.progress", progress.Completed, progress.Total);
    }

    private async Task RecordAsync(RenameTransaction transaction)
    {
        try
        {
            await _history.SaveAsync(transaction).ConfigureAwait(true);
            await _history.PruneAsync(_settings.Current.HistoryLimit).ConfigureAwait(true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The rename itself succeeded. Failing to write the log is not worth an error dialog,
            // but it does cost the user their undo after a restart, so it is worth a warning.
            _logger.LogWarning(error, "Could not record {Transaction} in the history", transaction.Id);
        }
    }

    /// <summary>Points the selection at the new names, so a second run builds on the first.</summary>
    private void AdvanceSelection(RenameTransaction transaction)
    {
        var moved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var operation in transaction.Operations) moved[operation.SourcePath] = operation.TargetPath;

        for (var index = 0; index < _sources.Count; index++)
        {
            var source = _sources[index];
            if (!moved.TryGetValue(source.Path, out var target)) continue;

            // Facts are re-read rather than carried across: renaming can touch the write time, and a
            // stale timestamp would silently poison the next batch's {modified} token.
            _sources[index] = source with { Path = target, Facts = FileFacts.Read(target, source.IsDirectory) };
        }
    }

    // ---- plumbing -------------------------------------------------------------------------------

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _debounce.Stop();
        _debounce.Tick -= OnDebounceTick;
        _localizer.PropertyChanged -= OnLanguageChanged;
        _previewCancellation?.Dispose();

        foreach (var card in Rules) Detach(card);
        Rules.Clear();
    }

    partial void OnPlanChanged(RenamePlan value) => NotifyRunState();

    partial void OnIsBusyChanged(bool value) => NotifyRunState();

    private void SetOption<T>(
        T value,
        T current,
        Action<T> assign,
        bool replan = true,
        [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(value, current)) return;

        assign(value);
        _settings.Save();
        OnPropertyChanged(property);

        if (replan) SchedulePreview();
    }

    private void NotifySourcesChanged()
    {
        OnPropertyChanged(nameof(SourceCount));
        OnPropertyChanged(nameof(HasSources));
    }

    private void NotifyRuleState()
    {
        OnPropertyChanged(nameof(HasRules));
        SavePresetCommand.NotifyCanExecuteChanged();
    }

    private void NotifyRunState()
    {
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(CanUndo));
        RunCommand.NotifyCanExecuteChanged();
        UndoLastCommand.NotifyCanExecuteChanged();
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Summary));
        foreach (var preset in Presets) preset.Refresh();
    }
}
