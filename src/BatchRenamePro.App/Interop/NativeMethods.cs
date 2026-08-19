using System.Runtime.InteropServices;

namespace BatchRenamePro.App.Interop;

#pragma warning disable CA1815 // These mirror Win32 structures; nothing compares or hashes them.

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MonitorInfo
{
    public int Size;
    public NativeRect Monitor;
    public NativeRect Work;
    public int Flags;
}

/// <summary>The reply to <c>WM_GETMINMAXINFO</c>: how large the window may grow.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MinMaxInfo
{
    public NativePoint Reserved;
    public NativePoint MaxSize;
    public NativePoint MaxPosition;
    public NativePoint MinTrackSize;
    public NativePoint MaxTrackSize;
}

/// <summary>How far the desktop frame reaches into the client area.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FrameMargins
{
    public int Left;
    public int Right;
    public int Top;
    public int Bottom;
}

#pragma warning restore CA1815

/// <summary>The handful of Win32 entry points a custom window frame needs.</summary>
internal static partial class NativeMethods
{
    public const int WM_GETMINMAXINFO = 0x0024;
    public const int WM_SETTINGCHANGE = 0x001A;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_NCHITTEST = 0x0084;

    public const int MONITOR_DEFAULTTONEAREST = 0x00000002;

    /// <summary>Draw the title bar and system menu in dark colours. Windows 10 1809 and later.</summary>
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    /// <summary>Round, square or small-radius window corners. Windows 11 and later.</summary>
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    /// <summary>Mica, acrylic or none. Windows 11 22H2 and later.</summary>
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    /// <summary>Colour of the one-pixel window border. Windows 11 and later.</summary>
    public const int DWMWA_BORDER_COLOR = 34;

    public const int DWMWCP_ROUND = 2;

    public const int DWMSBT_AUTO = 0;
    public const int DWMSBT_NONE = 1;
    public const int DWMSBT_MAINWINDOW = 2;       // Mica
    public const int DWMSBT_TRANSIENTWINDOW = 3;  // Acrylic

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmExtendFrameIntoClientArea(nint window, ref FrameMargins margins);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromWindow(nint window, int flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsZoomed(nint window);
}
