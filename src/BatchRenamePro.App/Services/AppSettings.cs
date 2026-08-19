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

    /// <summary>The folder the last "add folder" started from, so the picker opens where you left off.</summary>
    public string LastFolder { get; set; } = string.Empty;

    /// <summary>Window width in device-independent pixels.</summary>
    public double WindowWidth { get; set; } = 1280;

    /// <summary>Window height in device-independent pixels.</summary>
    public double WindowHeight { get; set; } = 820;

    /// <summary>Whether the window was maximized when it was last closed.</summary>
    public bool WindowMaximized { get; set; }

    /// <summary>Whether the navigation rail is showing labels.</summary>
    public bool NavigationExpanded { get; set; } = true;

    /// <summary>Copies the values, so a settings page can be cancelled without side effects.</summary>
    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>Forces every value back into a range the application can actually use.</summary>
    /// <remarks>Called after loading, because the file on disk is user-editable.</remarks>
    public void Normalize()
    {
        if (!Enum.IsDefined(Theme)) Theme = AppTheme.System;
        if (!Enum.IsDefined(Backdrop)) Backdrop = WindowBackdrop.Mica;
        if (!Enum.IsDefined(ScanTarget)) ScanTarget = ScanTarget.Files;
        if (!Enum.IsDefined(ConflictPolicy)) ConflictPolicy = ConflictPolicy.Block;

        Culture = string.IsNullOrWhiteSpace(Culture) ? string.Empty : Culture.Trim();
        HistoryLimit = Math.Clamp(HistoryLimit, 5, 500);

        // Small enough to be awkward is worse than the default; a hand-edited 40x10 window is unusable.
        WindowWidth = double.IsFinite(WindowWidth) ? Math.Clamp(WindowWidth, 960, 6000) : 1280;
        WindowHeight = double.IsFinite(WindowHeight) ? Math.Clamp(WindowHeight, 640, 4000) : 820;
    }
}
