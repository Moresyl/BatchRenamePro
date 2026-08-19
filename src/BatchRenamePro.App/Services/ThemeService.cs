using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace BatchRenamePro.App.Services;

/// <summary>Applies the colour palette and reports which one is in effect.</summary>
public interface IThemeService
{
    /// <summary>Whether the dark palette is the one currently loaded.</summary>
    bool IsDark { get; }

    /// <summary>The material actually in effect, which may be weaker than the one requested.</summary>
    WindowBackdrop EffectiveBackdrop { get; }

    /// <summary>Raised after the palette changes, so windows can re-apply their frame attributes.</summary>
    event EventHandler? Changed;

    /// <summary>Loads the palette for a setting, resolving <see cref="AppTheme.System"/> against Windows.</summary>
    /// <param name="theme">The requested scheme.</param>
    void Apply(AppTheme theme);

    /// <summary>Re-resolves <see cref="AppTheme.System"/>, for when Windows changes its app colour.</summary>
    void RefreshFromSystem();

    /// <summary>Records what the window manager granted, for the settings page to display.</summary>
    /// <param name="backdrop">The material that actually took effect.</param>
    void ReportBackdrop(WindowBackdrop backdrop);
}

/// <inheritdoc cref="IThemeService" />
public sealed class ThemeService : IThemeService
{
    private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    // Every palette dictionary declares this key. Looking the palette up by a marker rather than by
    // its position means the merge order in App.xaml can change without silently breaking theming.
    private const string PaletteMarker = "Theme.IsDark";

    private AppTheme _requested = AppTheme.System;

    /// <inheritdoc />
    public bool IsDark { get; private set; }

    /// <inheritdoc />
    public WindowBackdrop EffectiveBackdrop { get; private set; } = WindowBackdrop.None;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public void Apply(AppTheme theme)
    {
        _requested = theme;
        Load(theme is AppTheme.System ? SystemPrefersDark() : theme is AppTheme.Dark);
    }

    /// <inheritdoc />
    public void RefreshFromSystem()
    {
        if (_requested is AppTheme.System) Load(SystemPrefersDark());
    }

    /// <inheritdoc />
    public void ReportBackdrop(WindowBackdrop backdrop) => EffectiveBackdrop = backdrop;

    /// <summary>Reads the Windows "choose your default app mode" setting.</summary>
    /// <remarks>
    /// There is no managed API for this, and the registry value is missing on a fresh profile that
    /// has never opened the personalisation page — which means light.
    /// </remarks>
    public static bool SystemPrefersDark()
    {
        try
        {
            return Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1) is int light && light == 0;
        }
        catch (Exception error) when (error is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void Load(bool dark)
    {
        var application = Application.Current;
        if (application is null) return;

        var uri = new Uri(
            dark ? "Themes/Palette.Dark.xaml" : "Themes/Palette.Light.xaml",
            UriKind.Relative);

        var replacement = (ResourceDictionary)Application.LoadComponent(uri);
        var dictionaries = application.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(dictionary => dictionary.Contains(PaletteMarker));

        if (existing is null)
        {
            // Palettes go first so that anything merged after them — control styles, icons — can
            // reference their brushes with StaticResource.
            dictionaries.Insert(0, replacement);
        }
        else
        {
            dictionaries[dictionaries.IndexOf(existing)] = replacement;
        }

        IsDark = dark;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
