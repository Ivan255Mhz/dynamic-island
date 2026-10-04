using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Infrastructure;
using DynamicIsland.Interop;

namespace DynamicIsland.Views;

/// <summary>
/// Live eyedropper: a small panel with a Figma-like zoom grid and a swatch +
/// hex label that follows the cursor. Mouse and keyboard are tracked with
/// low-level hooks, so the panel never covers the sampled pixel, and colours
/// are read straight from the screen (no overlay tint, always up to date).
/// </summary>
public partial class ColorPickerOverlay : Window
{
    private const int Cells = 13;
    private const int Radius = 6;
    private const int RefreshMs = 50;
    private const double Gap = 18;

    private readonly byte[] _magnifierBuffer = new byte[Cells * Cells * 4];
    private readonly DispatcherTimer _refreshTimer;

    private NativeMethods.HookProc? _mouseCallback;
    private NativeMethods.HookProc? _keyboardCallback;
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;

    private IntPtr _captureDc;
    private IntPtr _captureBitmap;
    private IntPtr _captureBits;
    private IntPtr _previousBitmap;
    private bool _captureReady;

    private Point _cursor;
    private bool _closing;

    public ColorPickerOverlay()
    {
        InitializeComponent();

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(RefreshMs),
        };
        _refreshTimer.Tick += (_, _) => Update();

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    public Color? PickedColor { get; private set; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        InstallHooks();

        if (NativeMethods.GetCursorPos(out var point))
        {
            _cursor = new Point(point.X, point.Y);
        }
        else
        {
            _cursor = new Point(
                SystemParameters.VirtualScreenLeft + (SystemParameters.VirtualScreenWidth / 2),
                SystemParameters.VirtualScreenTop + (SystemParameters.VirtualScreenHeight / 2));
        }

        Update();
        _refreshTimer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closing = true;
        _refreshTimer.Stop();
        UninstallHooks();
        ReleaseCaptureSurface();
    }

    private void InstallHooks()
    {
        var module = NativeMethods.GetModuleHandle(null);

        _mouseCallback = OnMouseHook;
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseCallback, module, 0);

        _keyboardCallback = OnKeyboardHook;
        _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardCallback, module, 0);
    }

    private void UninstallHooks()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
    }

    private IntPtr OnMouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || _closing)
        {
            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        var message = (int)wParam;
        var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
        var point = new Point(data.Point.X, data.Point.Y);

        switch (message)
        {
            case NativeMethods.WM_MOUSEMOVE:
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                {
                    if (_closing)
                    {
                        return;
                    }

                    _cursor = point;
                    NativeMethods.SetCursor(NativeMethods.LoadCursor(IntPtr.Zero, NativeMethods.IDC_CROSS));
                    Update();
                });
                return new IntPtr(1);

            case NativeMethods.WM_LBUTTONDOWN:
                Dispatcher.BeginInvoke(DispatcherPriority.Input, FinishPick);
                return new IntPtr(1);

            case NativeMethods.WM_RBUTTONDOWN:
                Dispatcher.BeginInvoke(DispatcherPriority.Input, Cancel);
                return new IntPtr(1);

            case NativeMethods.WM_MOUSEWHEEL:
                return new IntPtr(1);
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr OnKeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || _closing)
        {
            return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        if ((int)wParam == NativeMethods.WM_KEYDOWN)
        {
            var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            if (data.VkCode == NativeMethods.VK_ESCAPE)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Input, Cancel);
                return new IntPtr(1);
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private void FinishPick()
    {
        if (_closing)
        {
            return;
        }

        PickedColor = SamplePixel((int)_cursor.X, (int)_cursor.Y) ?? Colors.Black;
        Close();
    }

    private void Cancel()
    {
        if (_closing)
        {
            return;
        }

        PickedColor = null;
        Close();
    }

    private void Update()
    {
        var color = UpdateMagnifier((int)_cursor.X, (int)_cursor.Y);
        if (color is { } c)
        {
            HexLabel.Text = ColorMath.ToHex(c);
            Preview.Background = new SolidColorBrush(c);
        }

        PlacePanel();
    }

    private void PlacePanel()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var cursorX = _cursor.X / dpi.DpiScaleX;
        var cursorY = _cursor.Y / dpi.DpiScaleY;
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualWidth = Math.Max(1, SystemParameters.VirtualScreenWidth);
        var virtualHeight = Math.Max(1, SystemParameters.VirtualScreenHeight);

        // Flip to the other side of the cursor when the panel would not fit.
        var left = cursorX + Gap + Width <= virtualWidth ? cursorX + Gap : cursorX - Gap - Width;
        var top = cursorY + Gap + Height <= virtualHeight ? cursorY + Gap : cursorY - Gap - Height;

        Left = virtualLeft + left;
        Top = virtualTop + top;
    }

    private Color? SamplePixel(int screenX, int screenY)
        => UpdateMagnifier(screenX, screenY);

    private Color? UpdateMagnifier(int screenX, int screenY)
    {
        if (!EnsureCaptureSurface())
        {
            return null;
        }

        Array.Clear(_magnifierBuffer);

        var right = (int)Math.Max(1, SystemParameters.VirtualScreenWidth);
        var bottom = (int)Math.Max(1, SystemParameters.VirtualScreenHeight);
        var sourceLeft = Math.Max(0, screenX - Radius);
        var sourceTop = Math.Max(0, screenY - Radius);
        var sourceRight = Math.Min(right, screenX + Radius + 1);
        var sourceBottom = Math.Min(bottom, screenY + Radius + 1);

        if (sourceRight > sourceLeft && sourceBottom > sourceTop)
        {
            var screenDc = NativeMethods.GetDC(IntPtr.Zero);
            if (screenDc != IntPtr.Zero)
            {
                try
                {
                    NativeMethods.BitBlt(
                        _captureDc,
                        sourceLeft - (screenX - Radius),
                        sourceTop - (screenY - Radius),
                        sourceRight - sourceLeft,
                        sourceBottom - sourceTop,
                        screenDc,
                        sourceLeft,
                        sourceTop,
                        0x00CC0020);
                }
                finally
                {
                    NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
                }
            }
        }

        Marshal.Copy(_captureBits, _magnifierBuffer, 0, _magnifierBuffer.Length);

        for (var i = 3; i < _magnifierBuffer.Length; i += 4)
        {
            _magnifierBuffer[i] = 255;
        }

        var frame = BitmapSource.Create(
            Cells,
            Cells,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            _magnifierBuffer,
            Cells * 4);
        frame.Freeze();
        MagnifierImage.Source = frame;

        if (screenX < 0 || screenY < 0 || screenX >= right || screenY >= bottom)
        {
            return null;
        }

        // The cursor always maps to the centre cell of the grid.
        var centerIndex = ((Radius * Cells) + Radius) * 4;
        return Color.FromRgb(
            _magnifierBuffer[centerIndex + 2],
            _magnifierBuffer[centerIndex + 1],
            _magnifierBuffer[centerIndex]);
    }

    private bool EnsureCaptureSurface()
    {
        if (_captureReady)
        {
            return true;
        }

        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            _captureDc = NativeMethods.CreateCompatibleDC(screenDc);
            if (_captureDc == IntPtr.Zero)
            {
                return false;
            }

            var info = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = Cells,
                biHeight = -Cells,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
            };

            _captureBitmap = NativeMethods.CreateDIBSection(
                screenDc,
                ref info,
                0,
                out _captureBits,
                IntPtr.Zero,
                0);

            if (_captureBitmap == IntPtr.Zero || _captureBits == IntPtr.Zero)
            {
                return false;
            }

            _previousBitmap = NativeMethods.SelectObject(_captureDc, _captureBitmap);
            _captureReady = true;
            return true;
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private void ReleaseCaptureSurface()
    {
        if (_captureDc != IntPtr.Zero)
        {
            if (_previousBitmap != IntPtr.Zero)
            {
                NativeMethods.SelectObject(_captureDc, _previousBitmap);
                _previousBitmap = IntPtr.Zero;
            }

            NativeMethods.DeleteDC(_captureDc);
            _captureDc = IntPtr.Zero;
        }

        if (_captureBitmap != IntPtr.Zero)
        {
            NativeMethods.DeleteObject(_captureBitmap);
            _captureBitmap = IntPtr.Zero;
        }

        _captureBits = IntPtr.Zero;
        _captureReady = false;
    }
}
