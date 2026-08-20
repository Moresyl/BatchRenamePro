using BatchRenamePro.Core.Planning;
using BatchRenamePro.Core.Scanning;

namespace BatchRenamePro.App.Services;

/// <summary>Which colour scheme the window uses.</summary>
public enum AppTheme
{
    /// <summary>Follow the Windows app-colour setting, and keep following it while running.</summary>
    System,

    /// <summary>Always light.</summary>
    Light,

    /// <summary>Always dark.</summary>
    Dark
}

/// <summary>The material Windows composites behind the window.</summary>
public enum WindowBackdrop
{
    /// <summary>An opaque background. Always available.</summary>
    None,

    /// <summary>Mica: the desktop wallpaper, heavily blurred. Windows 11 only.</summary>
    Mica,

    /// <summary>Acrylic: whatever is behind the window, blurred. Windows 11 only.</summary>
    Acrylic
}

/// <summary>Which folder chooser opens when a folder is added.</summary>
public enum FolderPickerStyle
{
    /// <summary>Windows' own folder dialog — the one every other application opens.</summary>
    System,

    /// <summary>The application's own picker, which lists the files in a folder as well as its subfolders.</summary>
    InApp
}

/// <summary>Everything the application remembers between runs.</summary>
/// <remarks>
/// A mutable class with defaults on every property, because it is deserialized from a file a user is
/// free to hand-edit: a missing or misspelled key has to leave a sensible value behind rather than a
/// zero. The defaults here are the ones a first-time user gets.
/// </remarks>
public sealed class AppSettings
{
    /// <summary>Bumped when a future version needs to migrate an older file.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Colour scheme.</summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Window material.</summary>
    public WindowBackdrop Backdrop { get; set; } = WindowBackdrop.Mica;

    /// <summary>UI language as a BCP-47 tag; empty means follow Windows.</summary>
    public string Culture { get; set; } = string.Empty;

    /// <summary>Whether a confirmation dialog appears before a batch runs.</summary>
    public bool ConfirmBeforeRun { get; set; } = true;

    /// <summary>Which folder chooser "add folder" opens.</summary>
    /// <remarks>
    /// The system dialog by default, because it is the one the user already knows: their own pinned
    /// places down the side, an address bar that takes a pasted path, and the keyboard habits every
    /// other application has taught them. What it will not do is list files, which is why the in-app
    /// picker exists — but that is a trade worth offering rather than one worth imposing.
    /// </remarks>
    public FolderPickerStyle FolderPicker { get; set; } = FolderPickerStyle.System;

    /// <summary>Whether adding a folder pulls in its subfolders too.</summary>
    public bool RecursiveByDefault { get; set; }

    /// <summary>What adding a folder collects.</summary>
    public ScanTarget ScanTarget { get; set; } = ScanTarget.Files;

    /// <summary>What happens when two items want the same name.</summary>
    public ConflictPolicy ConflictPolicy { get; set; } = ConflictPolicy.Block;

    /// <summary>How many past runs stay undoable.</summary>
    public int HistoryLimit { get; set; } = 50;

    /// <summary>Whether the preview hides rows the rules did not change.</summary>
    public bool ShowOnlyChanged { get; set; }

    /// <summary>Whether the public GitHub release channel is checked after startup.</summary>
    public bool CheckForUpdatesOnStartup { get; set; } = true;

    /// <summary>A release the user chose not to be reminded about.</summary>
    public string DismissedUpdateVersion { get; set; } = string.Empty;

    /// <summary>The folder the last "add folder" started from, so the picker opens where you left off.</summary>
    public string LastFolder { get; set; } = string.Empty;

    // There is no remembered window size or maximized flag here any more. The window is a fixed
    // 1000x640 that cannot be resized or maximized, so there was nothing left for those three values
    // to describe — they were being written on every exit and read by nobody. Old files still carry
    // them; the deserializer ignores what it does not recognise, and the next save drops them.

    /// <summary>Whether minimizing hides the window into the notification area instead of the taskbar.</summary>
    /// <remarks>
    /// Off by default. A window that vanishes from the taskbar when someone presses the minimize button
    /// is a surprise the first time it happens, and the icon Windows 11 puts it behind is hidden in the
    /// overflow flyout until the user drags it out — so the recovery is not obvious either.
    /// </remarks>
    public bool MinimizeToTray { get; set; }

    /// <summary>Copies the values, so a settings page can be cancelled without side effects.</summary>
    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>Forces every value back into a range the application can actually use.</summary>
    /// <remarks>Called after loading, because the file on disk is user-editable.</remarks>
    public void Normalize()
    {
        if (!Enum.IsDefined(Theme)) Theme = AppTheme.System;
        if (!Enum.IsDefined(Backdrop)) Backdrop = WindowBackdrop.Mica;
        if (!Enum.IsDefined(FolderPicker)) FolderPicker = FolderPickerStyle.System;
        if (!Enum.IsDefined(ScanTarget)) ScanTarget = ScanTarget.Files;
        if (!Enum.IsDefined(ConflictPolicy)) ConflictPolicy = ConflictPolicy.Block;

        Culture = string.IsNullOrWhiteSpace(Culture) ? string.Empty : Culture.Trim();
        DismissedUpdateVersion = string.IsNullOrWhiteSpace(DismissedUpdateVersion)
            ? string.Empty
            : DismissedUpdateVersion.Trim();
        HistoryLimit = Math.Clamp(HistoryLimit, 5, 500);
    }
}
