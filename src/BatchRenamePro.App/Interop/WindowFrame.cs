using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using BatchRenamePro.App.Services;

namespace BatchRenamePro.App.Interop;

/// <summary>
/// The Win32 half of the custom window frame: the desktop-composited backdrop, the dark title bar,
/// and the maximize fix.
/// </summary>
/// <remarks>
/// Everything here degrades quietly. <c>DwmSetWindowAttribute</c> returns a failure code on Windows
/// versions that do not know an attribute, and each method below treats that as "this machine does
/// not do backdrops" rather than as an error — so the same build runs on Windows 10 with an opaque
/// window and on Windows 11 with mica behind it.
/// </remarks>
public static class WindowFrame
{
    /// <summary>Whether the running build of Windows supports mica and acrylic backdrops.</summary>
    public static bool SupportsBackdrop { get; } =
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);

    /// <summary>Whether the running build of Windows supports rounded corners and a dark title bar.</summary>
    public static bool SupportsRoundedCorners { get; } =
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    /// <summary>Applies the dark title bar, rounded corners and the requested backdrop.</summary>
    /// <param name="window">A window that already has a handle.</param>
    /// <param name="dark">Whether the application is currently in its dark theme.</param>
    /// <param name="backdrop">The requested material.</param>
    /// <returns>The material actually in effect, which is <see cref="WindowBackdrop.None"/> when the request failed.</returns>
    public static WindowBackdrop Apply(Window window, bool dark, WindowBackdrop backdrop)
    {
        ArgumentNullException.ThrowIfNull(window);

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero) return WindowBackdrop.None;

        var useDark = dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));

        if (SupportsRoundedCorners)
        {
            var corners = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));
        }

        return ApplyBackdrop(window, handle, backdrop);
    }

    private static WindowBackdrop ApplyBackdrop(Window window, nint handle, WindowBackdrop backdrop)
    {
        if (backdrop is WindowBackdrop.None || !SupportsBackdrop)
        {
            var off = NativeMethods.DWMSBT_NONE;
            NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref off, sizeof(int));
            window.Background = Brushes.Transparent;
            return WindowBackdrop.None;
        }

        // The backdrop is painted by the desktop compositor *behind* the window, so it is only
        // visible where the window itself does not paint. Both of these are required: an opaque
        // WPF background hides it completely, and without the extended frame the compositor has
        // no client area to draw into.
        var margins = new FrameMargins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        if (NativeMethods.DwmExtendFrameIntoClientArea(handle, ref margins) != 0) return WindowBackdrop.None;

        var type = backdrop is WindowBackdrop.Acrylic
            ? NativeMethods.DWMSBT_TRANSIENTWINDOW
            : NativeMethods.DWMSBT_MAINWINDOW;

        if (NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int)) != 0)
            return WindowBackdrop.None;

        // The composition target has to become transparent too, or the material is drawn behind an
        // opaque surface and nothing is visible through it.
        var source = HwndSource.FromHwnd(handle);
        if (source?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
        window.Background = Brushes.Transparent;

        return backdrop;
    }

    /// <summary>
    /// Hooks the window's message loop so a maximized window stops at the edge of the monitor's
    /// work area.
    /// </summary>
    /// <param name="window">A window that already has a handle.</param>
    /// <remarks>
    /// A window with a custom <c>WindowChrome</c> maximizes to the size of the whole monitor plus
    /// the resize border, which pushes its edges — and the maximize button — off screen and covers
    /// the taskbar. Answering <c>WM_GETMINMAXINFO</c> with the work area is the fix, and it has to
    /// be recomputed per message because the answer changes when the window moves between monitors.
    /// </remarks>
    public static void TrackMaximizeBounds(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var handle = new WindowInteropHelper(window).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(Hook);
    }

    private static nint Hook(nint handle, int message, nint parameter, nint value, ref bool handled)
    {
        if (message != NativeMethods.WM_GETMINMAXINFO) return nint.Zero;

        var monitor = NativeMethods.MonitorFromWindow(handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == nint.Zero) return nint.Zero;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfoW(monitor, ref info)) return nint.Zero;

        var bounds = Marshal.PtrToStructure<MinMaxInfo>(value);
        bounds.MaxPosition.X = info.Work.Left - info.Monitor.Left;
        bounds.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
        bounds.MaxSize.X = info.Work.Right - info.Work.Left;
        bounds.MaxSize.Y = info.Work.Bottom - info.Work.Top;
        Marshal.StructureToPtr(bounds, value, fDeleteOld: false);

        handled = true;
        return nint.Zero;
    }
}
