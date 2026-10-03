using System.Runtime.InteropServices;

namespace DynamicIsland.Interop;

/// <summary>
/// Minimal system-tray icon implemented directly on top of Shell_NotifyIcon,
/// so no WinForms dependency is required. Mouse messages are delivered to the
/// owner window and surfaced as events.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    public const int CallbackMessage = 0x8000 + 1;

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    private const int NIM_ADD = 0x00000000;
    private const int NIM_DELETE = 0x00000002;
    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int IDI_APPLICATION = 32512;

    private readonly IntPtr _hwnd;
    private readonly int _id;
    private readonly bool _ownsIcon;
    private IntPtr _icon;
    private bool _added;

    public event EventHandler? MenuRequested;

    public event EventHandler? RestartRequested;

    public TrayIcon(IntPtr hwnd, string tooltip)
    {
        _hwnd = hwnd;
        _id = 1;
        _icon = LoadAppIcon(out _ownsIcon);

        var data = CreateData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _icon;
        data.szTip = tooltip;
        _added = Shell_NotifyIcon(NIM_ADD, ref data);
    }

    public bool HandleMessage(IntPtr lParam)
    {
        var message = lParam.ToInt32() & 0xFFFF;

        switch (message)
        {
            case WM_RBUTTONUP:
            case WM_LBUTTONUP:
                MenuRequested?.Invoke(this, EventArgs.Empty);
                return true;
            case WM_LBUTTONDBLCLK:
                RestartRequested?.Invoke(this, EventArgs.Empty);
                return true;
            default:
                return false;
        }
    }

    private NOTIFYICONDATA CreateData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = _id,
        szTip = string.Empty,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private static IntPtr LoadAppIcon(out bool ownsIcon)
    {
        ownsIcon = false;

        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                var count = ExtractIconEx(path, 0, out var large, out var small, 1);
                if (count > 0)
                {
                    if (small != IntPtr.Zero)
                    {
                        if (large != IntPtr.Zero)
                        {
                            DestroyIcon(large);
                        }

                        ownsIcon = true;
                        return small;
                    }

                    if (large != IntPtr.Zero)
                    {
                        ownsIcon = true;
                        return large;
                    }
                }
            }
        }
        catch
        {
            // Fall through to the shared default icon.
        }

        return LoadIcon(IntPtr.Zero, (IntPtr)IDI_APPLICATION);
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = CreateData();
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }

        if (_ownsIcon && _icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
        }

        _icon = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public int dwState;
        public int dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public int uVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
