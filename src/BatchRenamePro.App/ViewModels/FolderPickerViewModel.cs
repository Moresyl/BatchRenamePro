using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using BatchRenamePro.App.Localization;
using BatchRenamePro.Core.Sorting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BatchRenamePro.App.ViewModels;

/// <summary>One folder offered by the picker, either in the listing or down the side.</summary>
public sealed partial class FolderEntry : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Creates an entry.</summary>
    /// <param name="path">Full path; empty stands for the drive list.</param>
    /// <param name="name">What the row reads.</param>
    public FolderEntry(string path, string name)
    {
        Path = path;
        Name = name;
    }

    /// <summary>The folder's full path. Empty means "This PC".</summary>
    public string Path { get; }

    /// <summary>The label shown in the row.</summary>
    public string Name { get; }
}

/// <summary>
/// Drives the in-app folder picker: where it is, what is in there, and what the user has chosen.
/// </summary>
/// <remarks>
/// The reason this exists alongside the shell's <c>OpenFolderDialog</c> is the file list. A folder
/// chooser built on <c>FOS_PICKFOLDERS</c> lists folders and refuses to list anything else, so
/// standing in a folder of five hundred photos it shows an empty pane and Explorer's stock "no items
/// match your search" — which reads as "this folder is empty" or "the app cannot see my files" to
/// everyone who has not been told otherwise. Since choosing the right folder is the one decision
/// being made here, showing what is in it is not a nicety. So this picker lists both: folders on the
/// left to move through, the files themselves on the right to confirm you have arrived.
/// <para>
/// The shell dialog is still the default, and this is the setting you reach for once that has caught
/// you out — see <see cref="Services.AppSettings.FolderPicker" />. Familiarity beats the file list
/// when you already know which folder you want, and loses to it when you do not.
/// </para>
/// </remarks>
public sealed partial class FolderPickerViewModel : ObservableObject
{
    private readonly ILocalizer _localizer;
    private readonly List<FolderEntry> _tracked = [];

    [ObservableProperty]
    private string _location = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private string _fileHeader = string.Empty;

    [ObservableProperty]
    private string _acceptLabel = string.Empty;

    [ObservableProperty]
    private bool _canGoUp;

    private string _current = string.Empty;

    /// <summary>Creates the view model.</summary>
    /// <param name="localizer">Supplies the labels.</param>
    /// <param name="startingFolder">Where to open; empty starts at the drive list.</param>
    public FolderPickerViewModel(ILocalizer localizer, string startingFolder)
    {
        _localizer = localizer;

        foreach (var place in Places()) Shortcuts.Add(place);

        UpdateAccept();
        _ = LoadAsync(startingFolder ?? string.Empty);
    }

    /// <summary>Raised when the picker is done; the flag says whether anything was chosen.</summary>
    public event EventHandler<bool>? Finished;

    /// <summary>Subfolders of the folder being shown.</summary>
    public ObservableCollection<FolderEntry> Folders { get; } = [];

    /// <summary>The names of the files in the folder being shown. Display only — nothing is picked here.</summary>
    public ObservableCollection<string> Files { get; } = [];

    /// <summary>Drives and the usual user folders, as one-click jumps.</summary>
    public ObservableCollection<FolderEntry> Shortcuts { get; } = [];

    /// <summary>What the user settled on, once <see cref="Finished"/> has said so.</summary>
    public IReadOnlyList<string> Selection { get; private set; } = [];

    /// <summary>Goes to whatever path has been typed into the location box.</summary>
    [RelayCommand]
    public Task GoAsync() => LoadAsync(Location);

    /// <summary>Opens a folder from the listing or the shortcuts.</summary>
    /// <param name="entry">The folder to move into.</param>
    [RelayCommand]
    public Task OpenAsync(FolderEntry? entry) => entry is null ? Task.CompletedTask : LoadAsync(entry.Path);

    /// <summary>Moves to the containing folder, or to the drive list at the top.</summary>
    [RelayCommand]
    public Task GoUpAsync()
    {
        if (_current.Length == 0) return Task.CompletedTask;

        var parent = Directory.GetParent(_current);
        return LoadAsync(parent?.FullName ?? string.Empty);
    }

    /// <summary>Re-reads the folder being shown.</summary>
    [RelayCommand]
    public Task RefreshAsync() => LoadAsync(_current);

    /// <summary>Takes the ticked folders, or the current one when none are ticked.</summary>
    [RelayCommand]
    public void Accept()
    {
        var picked = Folders.Where(folder => folder.IsSelected).Select(folder => folder.Path).ToList();

        // Nothing ticked is not indecision — it is the ordinary case of having navigated into the
        // folder you want, which is exactly what the file list beside it was there to confirm.
        if (picked.Count == 0 && _current.Length > 0) picked.Add(_current);
        if (picked.Count == 0) return;

        Selection = picked;
        Finished?.Invoke(this, true);
    }

    /// <summary>Closes without choosing.</summary>
    [RelayCommand]
    public void Cancel() => Finished?.Invoke(this, false);

    private async Task LoadAsync(string path)
    {
        IsBusy = true;

        try
        {
            var listing = await Task.Run(() => Read(path)).ConfigureAwait(true);

            _current = listing.Path;
            Location = listing.Path;
            CanGoUp = listing.Path.Length > 0;

            foreach (var entry in _tracked) entry.PropertyChanged -= OnEntryChanged;
            _tracked.Clear();
            Folders.Clear();

            foreach (var folder in listing.Folders)
            {
                folder.PropertyChanged += OnEntryChanged;
                _tracked.Add(folder);
                Folders.Add(folder);
            }

            Files.Clear();
            foreach (var file in listing.Files) Files.Add(file);

            FileHeader = listing.Path.Length == 0
                ? _localizer["folderPicker.files"]
                : string.Format(CultureInfo.CurrentCulture, _localizer["folderPicker.filesCount"], listing.Files.Count);

            Message = listing.Error ?? string.Empty;

            UpdateAccept();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FolderEntry.IsSelected)) UpdateAccept();
    }

    private void UpdateAccept()
    {
        var ticked = Folders.Count(folder => folder.IsSelected);

        // The button says what it will do, because "choose" is ambiguous the moment more than one
        // row is highlighted and the folder you are standing in is also a candidate.
        AcceptLabel = ticked switch
        {
            0 => _localizer["folderPicker.chooseCurrent"],
            1 => _localizer["folderPicker.chooseOne"],
            _ => string.Format(CultureInfo.CurrentCulture, _localizer["folderPicker.chooseMany"], ticked)
        };
    }

    /// <summary>Reads a folder off the disk. Runs on a worker thread; touches nothing bindable.</summary>
    private static Listing Read(string path)
    {
        if (path.Length == 0) return new Listing(string.Empty, Drives(), [], null);

        try
        {
            var info = new DirectoryInfo(path);
            if (!info.Exists) return new Listing(string.Empty, Drives(), [], null);

            var folders = info.EnumerateDirectories()
                .Where(Listed)
                .OrderBy(folder => folder.Name, NaturalStringComparer.OrdinalIgnoreCase)
                .Select(folder => new FolderEntry(folder.FullName, folder.Name))
                .ToList();

            var files = info.EnumerateFiles()
                .Where(Listed)
                .OrderBy(file => file.Name, NaturalStringComparer.OrdinalIgnoreCase)
                .Select(file => file.Name)
                .ToList();

            return new Listing(info.FullName, folders, files, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or ArgumentException or NotSupportedException)
        {
            // A folder that cannot be read still has to leave the picker somewhere usable, so the
            // location stands and the reason is put on screen rather than thrown at the user.
            return new Listing(path, [], [], ex.Message);
        }
    }

    // Explorer hides these by default, and a picker that disagrees with Explorer about what is in a
    // folder is a picker people stop trusting.
    private static bool Listed(FileSystemInfo entry) =>
        (entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;

    private static List<FolderEntry> Drives() =>
    [
        .. DriveInfo.GetDrives()
            .Where(drive => drive.IsReady)
            .Select(drive => new FolderEntry(drive.RootDirectory.FullName, Describe(drive)))
    ];

    private static string Describe(DriveInfo drive)
    {
        var label = drive.VolumeLabel;
        var root = drive.RootDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar);

        return label.Length > 0 ? $"{label} ({root})" : root;
    }

    private List<FolderEntry> Places()
    {
        List<FolderEntry> places = [new(string.Empty, _localizer["folderPicker.thisPc"])];

        foreach (var folder in new[]
                 {
                     Environment.SpecialFolder.DesktopDirectory,
                     Environment.SpecialFolder.UserProfile,
                     Environment.SpecialFolder.MyDocuments,
                     Environment.SpecialFolder.MyPictures,
                     Environment.SpecialFolder.MyMusic,
                     Environment.SpecialFolder.MyVideos
                 })
        {
            var path = Environment.GetFolderPath(folder);
            if (path.Length == 0 || !Directory.Exists(path)) continue;

            places.Add(new FolderEntry(path, Path.GetFileName(path)));
        }

        // Downloads has no SpecialFolder of its own, and it is the folder people rename out of most.
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        if (Directory.Exists(downloads))
            places.Insert(2, new FolderEntry(downloads, Path.GetFileName(downloads)));

        return places;
    }

    private sealed record Listing(string Path, List<FolderEntry> Folders, List<string> Files, string? Error);
}
