using System.Runtime.CompilerServices;
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
internal struct MonitorInfo
{
    public int Size;
    public NativeRect Monitor;
    public NativeRect Work;
    public int Flags;
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

/*
    The three fixed-length text buffers inside NOTIFYICONDATAW. They are inline arrays of ushort
    rather than char so that the whole structure stays blittable and [LibraryImport] can generate a
    marshalling stub for it; a char array would need ByValTStr, which the source generator does not
    support. TrayIcon.Write copies a string into one of these.
*/

[InlineArray(128)]
internal struct TipBuffer
{
    private ushort _element;
}

[InlineArray(256)]
internal struct InfoBuffer
{
    private ushort _element;
}

[InlineArray(64)]
internal struct InfoTitleBuffer
{
    private ushort _element;
}

/// <summary>NOTIFYICONDATAW: one entry in the notification area.</summary>
/// <remarks>
/// Field order and padding have to match the Win32 declaration exactly — the shell reads
/// <c>cbSize</c> to decide which version of the structure it was handed, and a mismatch is reported
/// only as a silent failure to add the icon.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct NotifyIconData
{
    public int Size;
    public nint Window;
    public uint Id;
    public uint Flags;
    public uint CallbackMessage;
    public nint Icon;
    public TipBuffer Tip;
    public uint State;
    public uint StateMask;
    public InfoBuffer Info;
    public uint VersionOrTimeout;
    public InfoTitleBuffer InfoTitle;
    public uint InfoFlags;
    public Guid ItemGuid;
    public nint BalloonIcon;
}

#pragma warning restore CA1815

/// <summary>The handful of Win32 entry points a custom window frame needs.</summary>
internal static partial class NativeMethods
{
    public const int WM_SETTINGCHANGE = 0x001A;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_NCHITTEST = 0x0084;
    public const int WM_SYSCOMMAND = 0x0112;

    /// <summary>Maximize. Obeyed by <c>DefWindowProc</c> even without <c>WS_MAXIMIZEBOX</c>.</summary>
    public const int SC_MAXIMIZE = 0xF030;

    /// <summary>Begin resizing by keyboard or mouse — the Alt+Space "Size" command.</summary>
    public const int SC_SIZE = 0xF000;

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

    // ---- Notification area ------------------------------------------------------------------

    /// <summary>The private message the tray icon reports mouse and keyboard activity through.</summary>
    /// <remarks>WM_APP and above are reserved for the application, so nothing else can collide.</remarks>
    public const int WM_TRAYICON = 0x8000 + 1;

    public const int WM_CONTEXTMENU = 0x007B;

    /// <summary>Sent instead of WM_LBUTTONUP once the icon is on version 4.</summary>
    public const int NIN_SELECT = 0x0400;

    /// <summary>The keyboard equivalent of <see cref="NIN_SELECT"/>: space or enter on the icon.</summary>
    public const int NIN_KEYSELECT = 0x0401;

    public const int NIM_ADD = 0;
    public const int NIM_MODIFY = 1;
    public const int NIM_DELETE = 2;
    public const int NIM_SETVERSION = 4;

    public const uint NIF_MESSAGE = 0x01;
    public const uint NIF_ICON = 0x02;
    public const uint NIF_TIP = 0x04;

    /// <summary>Ask the shell to show the tooltip itself, which version 4 otherwise suppresses.</summary>
    public const uint NIF_SHOWTIP = 0x80;

    public const uint NOTIFYICON_VERSION_4 = 4;

    public const int SM_CXSMICON = 49;

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShellNotifyIcon(int message, ref NotifyIconData data);

    /// <summary>
    /// Registers the <c>TaskbarCreated</c> broadcast, which the shell sends to every top-level
    /// window when Explorer restarts. An icon added before that point is gone and must be re-added.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessage(string name);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int index);

    /// <summary>Builds an icon from one image inside a .ico, scaling it to the size asked for.</summary>
    [LibraryImport("user32.dll")]
    public static partial nint CreateIconFromResourceEx(
        ref byte bits,
        uint size,
        [MarshalAs(UnmanagedType.Bool)] bool isIcon,
        uint version,
        int width,
        int height,
        uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint window);
}
