using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using BatchRenamePro.App.Services;

namespace BatchRenamePro.App.Interop;

/// <summary>
/// The Win32 half of the custom window frame: the desktop-composited backdrop, the dark title bar,
/// and keeping a fixed-size window inside the screen it opened on.
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

    /// <summary>Shrinks a window that is larger than the screen it opened on, and keeps it centred.</summary>
    /// <param name="window">A window that already has a handle.</param>
    /// <remarks>
    /// The application's windows are a fixed size and cannot be dragged larger or smaller, so the size
    /// they start at is the size they keep. A design drawn for a comfortable desktop does not fit a
    /// small laptop at 150% scaling, and with the resize border gone there would be no way out of
    /// that — the buttons along the bottom would simply be under the taskbar. Growing is somebody
    /// else's decision; not overflowing the screen is not optional.
    /// </remarks>
    public static void FitToWorkArea(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var work = WorkArea(window);
        if (work.IsEmpty) return;

        var width = Math.Min(window.Width, work.Width);
        var height = Math.Min(window.Height, work.Height);
        if (width >= window.Width && height >= window.Height) return;

        // Adjusted relative to wherever the window was already placed rather than recomputed from the
        // monitor's origin: shrinking anchors the top-left corner, so handing back half of what each
        // side gave up leaves it centred on whatever it was centred on, on any arrangement of screens.
        if (double.IsFinite(window.Left)) window.Left += (window.Width - width) / 2;
        if (double.IsFinite(window.Top)) window.Top += (window.Height - height) / 2;

        window.Width = width;
        window.Height = height;
    }

    /// <summary>Refuses the maximize and resize commands, whoever sends them.</summary>
    /// <param name="window">A window that already has a handle.</param>
    /// <remarks>
    /// <c>ResizeMode</c> takes away <c>WS_MAXIMIZEBOX</c> and <c>WS_THICKFRAME</c>, and that is enough
    /// to stop every gesture a user has: there is no maximize button, no resize border, and Windows
    /// checks those bits before honouring a caption double-click, Win+Up or a drag against the top of
    /// the screen. What it does not stop is the command arriving directly — <c>DefWindowProc</c> obeys
    /// an <c>SC_MAXIMIZE</c> it is handed regardless of the style bits, which is measurably true: this
    /// window maximizes to full screen if you post one at it.
    /// <para>
    /// Nothing the user does posts one. Window managers, accessibility tools and UI Automation's
    /// <c>SetWindowVisualState</c> all do, and a window laid out for one size has no business being
    /// stretched by a tool the user installed for something else. So the two commands are dropped
    /// here, where refusing them costs nothing and leaves the state machine untouched — as against
    /// snapping back from <c>OnStateChanged</c>, which lets the window maximize and then visibly
    /// un-maximize.
    /// </para>
    /// </remarks>
    public static void ForbidResizing(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero) return;

        HwndSource.FromHwnd(handle)?.AddHook(RefuseSizeCommands);
    }

    private static nint RefuseSizeCommands(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != NativeMethods.WM_SYSCOMMAND) return nint.Zero;

        // The low four bits are used internally by Windows and have to be masked off before the
        // command is recognisable — a mouse-driven SC_MAXIMIZE arrives as 0xF032, not 0xF030.
        var command = (int)wParam & 0xFFF0;
        if (command is NativeMethods.SC_MAXIMIZE or NativeMethods.SC_SIZE) handled = true;

        return nint.Zero;
    }

    /// <summary>The usable area of the monitor a window is on, in device-independent units.</summary>
    /// <remarks>
    /// Not <see cref="SystemParameters.WorkArea" />, which answers for the primary monitor and is
    /// therefore the wrong answer the moment a window opens on the second one. The conversion out of
    /// physical pixels uses the window's own scaling, which on a mixed-DPI desktop is not the system's.
    /// </remarks>
    private static Size WorkArea(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero) return Size.Empty;

        var monitor = NativeMethods.MonitorFromWindow(handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == nint.Zero) return Size.Empty;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfoW(monitor, ref info)) return Size.Empty;

        var scale = VisualTreeHelper.GetDpi(window);
        if (scale.DpiScaleX <= 0 || scale.DpiScaleY <= 0) return Size.Empty;

        return new Size(
            (info.Work.Right - info.Work.Left) / scale.DpiScaleX,
            (info.Work.Bottom - info.Work.Top) / scale.DpiScaleY);
    }
}
