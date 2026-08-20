using System.IO;
using System.Windows;
using BatchRenamePro.App.Localization;
using BatchRenamePro.App.Views;
using Microsoft.Win32;

namespace BatchRenamePro.App.Services;

/// <summary>Asks the user things: in-window dialogs, and the system file pickers.</summary>
public interface IDialogService
{
    /// <summary>Asks a yes-or-no question.</summary>
    /// <param name="title">The heading.</param>
    /// <param name="message">The body text.</param>
    /// <param name="confirmKey">Localization key for the confirm button; defaults to OK.</param>
    /// <param name="destructive">Whether the confirm button should read as dangerous.</param>
    Task<bool> ConfirmAsync(string title, string message, string confirmKey = "action.ok", bool destructive = false);

    /// <summary>Asks for a line of text.</summary>
    /// <param name="title">The heading.</param>
    /// <param name="message">What to type.</param>
    /// <param name="initial">The text the box starts with.</param>
    /// <returns>The text, or <see langword="null"/> if dismissed.</returns>
    Task<string?> PromptAsync(string title, string message, string initial = "");

    /// <summary>States something and waits for acknowledgement.</summary>
    /// <param name="title">The heading.</param>
    /// <param name="message">The body text.</param>
    /// <param name="kind">What the message is about.</param>
    Task NotifyAsync(string title, string message, DialogKind kind = DialogKind.Warning);

    /// <summary>Opens the folder picker.</summary>
    /// <param name="startingFolder">Where the picker opens.</param>
    /// <returns>The chosen folders, empty if cancelled.</returns>
    IReadOnlyList<string> PickFolders(string? startingFolder);

    /// <summary>Opens the multi-select file picker.</summary>
    /// <param name="startingFolder">Where the picker opens.</param>
    IReadOnlyList<string> PickFiles(string? startingFolder);

    /// <summary>Opens the file picker for a single file.</summary>
    /// <param name="filter">A Win32 dialog filter string.</param>
    string? PickOpenFile(string filter);

    /// <summary>Opens the save-as picker.</summary>
    /// <param name="filter">A Win32 dialog filter string.</param>
    /// <param name="suggestedName">The name the box starts with.</param>
    string? PickSaveFile(string filter, string suggestedName);
}

/// <inheritdoc cref="IDialogService" />
public sealed class DialogService : IDialogService
{
    private readonly DialogHost _host;
    private readonly ILocalizer _localizer;
    private readonly IThemeService _theme;
    private readonly ISettingsService _settings;

    /// <summary>Creates the service.</summary>
    /// <param name="host">Where in-window dialogs are shown.</param>
    /// <param name="localizer">Supplies the button labels.</param>
    /// <param name="theme">Tells the folder picker which way round to draw its frame.</param>
    /// <param name="settings">Says which of the two folder pickers to open.</param>
    public DialogService(DialogHost host, ILocalizer localizer, IThemeService theme, ISettingsService settings)
    {
        _host = host;
        _localizer = localizer;
        _theme = theme;
        _settings = settings;
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(string title, string message, string confirmKey = "action.ok", bool destructive = false)
    {
        var answer = await _host.ShowAsync(new DialogRequest(title, message, destructive ? DialogKind.Warning : DialogKind.Question)
        {
            ConfirmLabel = _localizer[confirmKey],
            CancelLabel = _localizer["action.cancel"],
            IsDestructive = destructive
        }).ConfigureAwait(true);

        return answer is not null;
    }

    /// <inheritdoc />
    public async Task<string?> PromptAsync(string title, string message, string initial = "")
    {
        var request = new DialogRequest(title, message)
        {
            IsInput = true,
            Input = initial,
            ConfirmLabel = _localizer["action.ok"],
            CancelLabel = _localizer["action.cancel"]
        };

        var answer = await _host.ShowAsync(request).ConfigureAwait(true);
        return string.IsNullOrWhiteSpace(answer) ? null : answer.Trim();
    }

    /// <inheritdoc />
    public Task NotifyAsync(string title, string message, DialogKind kind = DialogKind.Warning) =>
        _host.ShowAsync(new DialogRequest(title, message, kind)
        {
            ConfirmLabel = _localizer["action.dismiss"]
        });

    /// <inheritdoc />
    /// <remarks>
    /// Two pickers, chosen by <see cref="AppSettings.FolderPicker" />, and the shell's is the default.
    /// It is the dialog every other application opens: the user's own pinned places are down the side,
    /// the address bar takes a pasted path, and none of it has to be learned. What it will not do is
    /// list files — <c>FOS_PICKFOLDERS</c> shows folders and nothing else — so standing in a folder of
    /// photos it presents an empty pane and Explorer's stock "no items match your search", which reads
    /// as "this folder is empty" to anyone who has not been told otherwise.
    /// <para>
    /// That is what <see cref="FolderPickerWindow" /> is for, and it is one setting away. Which of the
    /// two costs less is a question about the person, not about the code: familiarity is worth more to
    /// someone who picks a folder they already know, and the file list is worth more to someone who is
    /// looking for the right one.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> PickFolders(string? startingFolder) =>
        _settings.Current.FolderPicker is FolderPickerStyle.InApp
            ? PickFoldersInApp(startingFolder)
            : PickFoldersWithShell(startingFolder);

    /// <inheritdoc />
    public IReadOnlyList<string> PickFiles(string? startingFolder)
    {
        var dialog = new OpenFileDialog
        {
            Title = _localizer["source.addFiles"],
            Multiselect = true,
            CheckFileExists = true,
            InitialDirectory = Existing(startingFolder)
        };

        return dialog.ShowDialog() is true ? dialog.FileNames : [];
    }

    /// <inheritdoc />
    public string? PickOpenFile(string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        return dialog.ShowDialog() is true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public string? PickSaveFile(string filter, string suggestedName)
    {
        var dialog = new SaveFileDialog { Filter = filter, FileName = suggestedName, OverwritePrompt = true };
        return dialog.ShowDialog() is true ? dialog.FileName : null;
    }

    // Returns the array rather than the interface because that is what the dialog already hands
    // back, and wrapping it only to widen it is a copy the caller does not need.
    private string[] PickFoldersWithShell(string? startingFolder)
    {
        var dialog = new OpenFolderDialog
        {
            Title = _localizer["source.addFolder"],
            Multiselect = true,
            InitialDirectory = Existing(startingFolder)
        };

        return dialog.ShowDialog() is true ? dialog.FolderNames : [];
    }

    private IReadOnlyList<string> PickFoldersInApp(string? startingFolder)
    {
        var picker = new FolderPickerWindow(_localizer, Existing(startingFolder), _theme);

        // An owner that is not on screen yet cannot be centred on, and assigning one throws.
        if (Application.Current?.MainWindow is { IsLoaded: true } owner && !ReferenceEquals(owner, picker))
            picker.Owner = owner;
        else
            picker.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        return picker.ShowDialog() is true ? picker.Selection : [];
    }

    // A stale InitialDirectory is ignored by the shell dialog, but checking keeps the picker
    // opening at "This PC" rather than somewhere arbitrary when the remembered folder is gone.
    private static string Existing(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) ? path : string.Empty;
}
