using System.Windows;
using System.Windows.Interop;

namespace BatchRenamePro.App.Interop;

/// <summary>The application's icon in the notification area, and the clicks it reports back.</summary>
/// <remarks>
/// <para>
/// The icon belongs to a window, not to a process, and the window it belongs to here is a hidden
/// tool window of its own rather than the shell window. That separation is the point: the shell
/// window is hidden while the icon is showing, and an icon whose owner is hidden stops receiving
/// the messages it needs.
/// </para>
/// <para>
/// The window is top-level rather than message-only, which costs nothing and buys one thing — a
/// message-only window cannot be brought to the foreground, and a context menu opened from a
/// background window is one the user cannot dismiss by clicking away from it.
/// </para>
/// </remarks>
internal sealed class TrayIcon : IDisposable
{
    private static readonly Uri IconResource =
        new("pack://application:,,,/Assets/app.ico", UriKind.Absolute);

    /// <summary>WS_POPUP: a top-level window with no frame to draw.</summary>
    private const int PopupWindow = unchecked((int)0x80000000);

    /// <summary>WS_EX_TOOLWINDOW: keeps it out of the taskbar and out of alt-tab.</summary>
    private const int ToolWindow = 0x00000080;

    /// <summary>Only ever one icon, so its identifier within the window is a constant.</summary>
    private const uint IconId = 1;

    private readonly uint _taskbarCreated;
    private readonly HwndSource _source;

    private string _tooltip;
    private nint _icon;
    private bool _visible;
    private bool _disposed;

    /// <summary>Creates the icon's owner window and loads the picture. Nothing is shown yet.</summary>
    /// <param name="tooltip">The text the shell shows when the pointer rests on the icon.</param>
    public TrayIcon(string tooltip)
    {
        ArgumentNullException.ThrowIfNull(tooltip);

        _tooltip = tooltip;
        _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        var parameters = new HwndSourceParameters("BatchRenamePro.TrayIcon")
        {
            Width = 0,
            Height = 0,
            WindowStyle = PopupWindow,
            ExtendedWindowStyle = ToolWindow
        };

        _source = new HwndSource(parameters);
        _source.AddHook(OnMessage);

        // Whatever the shell considers a small icon at the current scale: 16 at 100%, 20 at 125%.
        var size = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON);
        _icon = IconLoader.Load(IconResource, size > 0 ? size : 16);
    }

    /// <summary>Raised when the icon is clicked or chosen from the keyboard.</summary>
    public event EventHandler? Selected;

    /// <summary>Raised on a right-click, carrying the cursor position in device-independent pixels.</summary>
    public event EventHandler<Point>? ContextMenuRequested;

    /// <summary>The hidden window that owns the icon, for handing to <c>SetForegroundWindow</c>.</summary>
    public nint Handle => _source.Handle;

    /// <summary>The text the shell shows when the pointer rests on the icon.</summary>
    /// <remarks>Setting this while the icon is showing updates it in place, so switching the
    /// application's language does not leave the previous one's name behind in the tooltip.</remarks>
    public string Tooltip
    {
        get => _tooltip;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (string.Equals(_tooltip, value, StringComparison.Ordinal)) return;

            _tooltip = value;
            if (!_visible) return;

            var data = Describe(NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP);
            _ = NativeMethods.ShellNotifyIcon(NativeMethods.NIM_MODIFY, ref data);
        }
    }

    /// <summary>Puts the icon in the notification area. Does nothing if it is already there.</summary>
    public void Show()
    {
        if (_disposed || _visible) return;

        var data = Describe(
            NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON |
            NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP);

        if (!NativeMethods.ShellNotifyIcon(NativeMethods.NIM_ADD, ref data)) return;

        _visible = true;

        // Version 4 changes what the callback reports: a right-click arrives as WM_CONTEXTMENU with
        // the cursor position already in it, and keyboard selection arrives at all.
        var version = Describe(0);
        version.VersionOrTimeout = NativeMethods.NOTIFYICON_VERSION_4;
        _ = NativeMethods.ShellNotifyIcon(NativeMethods.NIM_SETVERSION, ref version);
    }

    /// <summary>Takes the icon out of the notification area.</summary>
    public void Hide()
    {
        if (!_visible) return;

        var data = Describe(0);
        _ = NativeMethods.ShellNotifyIcon(NativeMethods.NIM_DELETE, ref data);
        _visible = false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Hide();
        _source.RemoveHook(OnMessage);
        _source.Dispose();

        if (_icon != 0)
        {
            _ = NativeMethods.DestroyIcon(_icon);
            _icon = 0;
        }

        GC.SuppressFinalize(this);
    }

    private NotifyIconData Describe(uint flags)
    {
        var data = new NotifyIconData
        {
            Size = System.Runtime.InteropServices.Marshal.SizeOf<NotifyIconData>(),
            Window = _source.Handle,
            Id = IconId,
            Flags = flags,
            CallbackMessage = NativeMethods.WM_TRAYICON,
            Icon = _icon
        };

        Write(data.Tip, _tooltip);
        return data;
    }

    private static void Write(Span<ushort> destination, string value)
    {
        var length = Math.Min(value.Length, destination.Length - 1);
        for (var i = 0; i < length; i++) destination[i] = value[i];
        destination[length] = 0;
    }

    private nint OnMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        // Explorer restarting wipes the notification area. Every icon that was in it has to be put
        // back by its owner, and an application that does not is the one whose icon "disappears".
        if (_taskbarCreated != 0 && (uint)message == _taskbarCreated && _visible)
        {
            _visible = false;
            Show();
            return 0;
        }

        if (message != NativeMethods.WM_TRAYICON) return 0;

        switch ((int)(lParam & 0xFFFF))
        {
            case NativeMethods.NIN_SELECT:
            case NativeMethods.NIN_KEYSELECT:
                Selected?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;

            case NativeMethods.WM_CONTEXTMENU:
                // Signed: a second monitor to the left of the primary one has negative coordinates.
                var x = (short)(wParam & 0xFFFF);
                var y = (short)((wParam >> 16) & 0xFFFF);
                ContextMenuRequested?.Invoke(this, ToDeviceIndependent(x, y));
                handled = true;
                break;

            default:
                break;
        }

        return 0;
    }

    private Point ToDeviceIndependent(int x, int y)
    {
        var target = _source.CompositionTarget;
        return target is null ? new Point(x, y) : target.TransformFromDevice.Transform(new Point(x, y));
    }
}
