using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Animations;
using DynamicIsland.Infrastructure;
using DynamicIsland.Interop;
using DynamicIsland.Models;
using DynamicIsland.Services;
using DynamicIsland.ViewModels;

namespace DynamicIsland;

public partial class MainWindow : Window
{
    private const double RailWidth = 240;
    private const double MiniWidth = 252;
    private const double CollapsedHeight = 40;
    private const double CollapsedRadius = 20;

    private const double DetailWidth = 380;
    private const double MusicHeight = 150;
    private const double ScreenshotsHeight = 136;
    private const double ClipboardHeight = 238;
    private const double TranslatorHeight = 260;
    private const double ColorPickerWidth = 380;
    private const double ColorPickerHeight = 204;
    private const double ExpandedRadius = 22;

    private const double ExpandMs = 460;
    private const double CollapseMs = 320;
    private const double MorphMs = 260;
    private const double UnfoldMs = 220;
    private const double ContentFadeMs = 260;

    private static readonly SpringEase ExpandSpring = new() { Damping = 1.0, Frequency = 10.5 };
    private static readonly SpringEase MorphSpring = new() { Damping = 1.0, Frequency = 14.0 };
    private static readonly SpringEase UnfoldSpring = new() { Damping = 1.0, Frequency = 12.0 };
    private static readonly CubicEase EaseOut = new() { EasingMode = EasingMode.EaseOut };
    private static readonly CubicEase CollapseEase = new() { EasingMode = EasingMode.EaseInOut };
    private static readonly QuadraticEase EaseIn = new() { EasingMode = EasingMode.EaseIn };

    private readonly DispatcherTimer _hoverTimer;
    private readonly AppSettings _settings;
    private readonly IScreenColorPicker _screenPicker;
    private readonly FrameworkElement[] _sections;
    private readonly Dictionary<IslandDock, MenuItem> _dockItems = new();

    private MainViewModel? _viewModel;
    private bool _isExpanded;
    private bool _isPinned;
    private int _outsideHoverTicks;
    private IslandDock _dock;
    private TrayIcon? _tray;
    private ContextMenu? _trayMenu;
    private MenuItem? _autostartItem;
    private bool _hotkeyRegistered;
    private IntPtr _handle;

    private const int EyedropperHotkeyId = 0x4D49;

    public MainWindow(IScreenColorPicker screenPicker)
    {
        InitializeComponent();

        if (Environment.GetEnvironmentVariable("DI_NOTRANS") == "1")
        {
            AllowsTransparency = false;
            Background = Brushes.Black;
        }

        _screenPicker = screenPicker;
        _screenPicker.PickStarted += (_, _) => EnterPicking();
        _screenPicker.PickFinished += (_, _) => ExitPicking();

        _sections = new FrameworkElement[]
        {
            MusicSection,
            ClipboardSection,
            ScreenshotsSection,
            TranslatorSection,
            ColorPickerSection,
        };

        foreach (var element in _sections)
        {
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = new ScaleTransform(1, 1);
        }

        _ = Task.Run(Views.ColorWheel.Prewarm);

        _settings = SettingsStore.Current;
        _dock = _settings.Dock;
        BorderEx.Dock = _dock;

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _hoverTimer.Tick += OnHoverTimerTick;

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        DataContextChanged += OnDataContextChanged;

        var probeMode = Environment.GetEnvironmentVariable("DI_FPS");
        if (probeMode is "1" or "2" or "3")
        {
            Loaded += (_, _) => EnableFpsProbe(probeMode);
        }
    }

    private void EnableFpsProbe(string mode)
    {
        _probeActive = true;
        _probeLast = DateTime.Now;
        _probeGc0 = GC.CollectionCount(0);
        _probeGc1 = GC.CollectionCount(1);
        _probeGc2 = GC.CollectionCount(2);
        CompositionTarget.Rendering += OnProbeRendering;

        var logger = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        logger.Tick += (_, _) =>
        {
            var fps = _probeFrames / 2.0;
            var maxGap = _probeMaxGap;
            var buckets = string.Join("/", _probeBuckets);

            var gc0 = GC.CollectionCount(0);
            var gc1 = GC.CollectionCount(1);
            var gc2 = GC.CollectionCount(2);
            var d0 = gc0 - _probeGc0;
            var d1 = gc1 - _probeGc1;
            var d2 = gc2 - _probeGc2;
            _probeGc0 = gc0;
            _probeGc1 = gc1;
            _probeGc2 = gc2;

            var pPolls = Infrastructure.Diag.ClipboardPolls - _diagPolls;
            var pReads = Infrastructure.Diag.ClipboardReads - _diagReads;
            var pChanges = Infrastructure.Diag.ClipboardChanges - _diagChanges;
            var pMedia = Infrastructure.Diag.MediaRefreshes - _diagMedia;
            var pProps = Infrastructure.Diag.MediaPropertyFetches - _diagProps;
            var pArt = Infrastructure.Diag.MediaArtworkDecodes - _diagArt;
            var pShots = Infrastructure.Diag.ScreenshotRefreshes - _diagShots;
            _diagPolls = Infrastructure.Diag.ClipboardPolls;
            _diagReads = Infrastructure.Diag.ClipboardReads;
            _diagChanges = Infrastructure.Diag.ClipboardChanges;
            _diagMedia = Infrastructure.Diag.MediaRefreshes;
            _diagProps = Infrastructure.Diag.MediaPropertyFetches;
            _diagArt = Infrastructure.Diag.MediaArtworkDecodes;
            _diagShots = Infrastructure.Diag.ScreenshotRefreshes;

            var slow = _probeSlow.Count == 0 ? "-" : string.Join(" | ", _probeSlow);
            var line = $"fps={fps:F1} maxGap={maxGap:F0} <17/20/25/33/50/>50={buckets} gc={d0}/{d1}/{d2} clip={pPolls}/{pReads}/{pChanges} media={pMedia}/{pProps}/{pArt} mgr={Infrastructure.Diag.MediaManagerReady} shots={pShots} slow[>25ms]: {slow}";

            _probeFrames = 0;
            _probeMaxGap = 0;
            Array.Clear(_probeBuckets, 0, _probeBuckets.Length);
            _probeSlow.Clear();

            Task.Run(() => System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "di_fps.log"), line + "\n"));
        };
        logger.Start();

        if (mode == "1")
        {
            var toggle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            toggle.Tick += (_, _) =>
            {
                if (_isExpanded)
                {
                    Collapse();
                }
                else
                {
                    _isPinned = true;
                    Expand();
                }
            };
            toggle.Start();
        }
        else if (mode == "3")
        {
            var sections = new IslandSection[]
            {
                IslandSection.Music,
                IslandSection.Clipboard,
                IslandSection.Translator,
                IslandSection.Screenshots,
                IslandSection.ColorPicker,
            };
            var index = 0;

            _isPinned = true;
            Expand();

            var cycle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            cycle.Tick += (_, _) =>
            {
                index = (index + 1) % sections.Length;
                if (_viewModel is not null)
                {
                    _viewModel.SelectedSection = sections[index];
                }
            };
            cycle.Start();
        }
    }

    private long _probeFrames;
    private double _probeMaxGap;
    private DateTime _probeLast;
    private readonly int[] _probeBuckets = new int[6];
    private readonly List<string> _probeSlow = new();
    private int _probeGc0;
    private int _probeGc1;
    private int _probeGc2;
    private int _diagPolls;
    private int _diagReads;
    private int _diagChanges;
    private int _diagMedia;
    private int _diagProps;
    private int _diagArt;
    private int _diagShots;
    private DateTime _transitionStart;
    private string _transitionKind = "-";

    private void MarkTransition(string kind)
    {
        if (!_probeActive)
        {
            return;
        }

        _transitionStart = DateTime.Now;
        _transitionKind = kind;
    }

    private bool _probeActive;

    private void OnProbeRendering(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        var dt = (now - _probeLast).TotalMilliseconds;
        _probeLast = now;
        _probeFrames++;
        if (dt > _probeMaxGap)
        {
            _probeMaxGap = dt;
        }

        var bucket = dt < 17 ? 0 : dt < 20 ? 1 : dt < 25 ? 2 : dt < 33 ? 3 : dt < 50 ? 4 : 5;
        _probeBuckets[bucket]++;

        if (dt > 25 && _probeSlow.Count < 6)
        {
            var since = _transitionKind == "-" ? -1 : (now - _transitionStart).TotalMilliseconds;
            _probeSlow.Add($"{dt:F0}ms@{_transitionKind}+{since:F0} island={Island.Width:F0}x{Island.Height:F0}");
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _handle = handle;
        var style = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
        style |= NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLong(handle, NativeMethods.GWL_EXSTYLE, style);

        HwndSource.FromHwnd(handle)?.AddHook(WndProc);

        _hotkeyRegistered = NativeMethods.RegisterHotKey(
            handle,
            EyedropperHotkeyId,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT,
            NativeMethods.VK_C);

        _tray = new TrayIcon(handle, "Dynamic Island");
        _tray.Activated += (_, _) =>
        {
            _isPinned = true;
            Expand();
        };
        _tray.MenuRequested += (_, _) => ShowTrayMenu();
        _tray.RestartRequested += (_, _) => RestartApplication();

        UpdateTrayToolTip();

        Closed += (_, _) =>
        {
            if (_hotkeyRegistered)
            {
                _hotkeyRegistered = false;
                NativeMethods.UnregisterHotKey(_handle, EyedropperHotkeyId);
            }

            _tray?.Dispose();
            _tray = null;
        };
    }

    private void UpdateTrayToolTip()
    {
        if (_tray is null)
        {
            return;
        }

        var music = _viewModel?.Music;
        var tip = "Dynamic Island";

        if (music is not null && music.Track.HasTrack)
        {
            tip = $"Dynamic Island — {music.Track.DisplayTitle} · {music.Track.DisplayArtist}";
            if (music.IsMuted)
            {
                tip += " (без звука)";
            }
        }

        _tray.SetToolTip(tip);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(NativeMethods.MA_NOACTIVATE);
        }

        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == EyedropperHotkeyId)
        {
            handled = true;
            StartEyedropperFromHotkey();
            return IntPtr.Zero;
        }

        if (msg == TrayIcon.CallbackMessage)
        {
            handled = _tray?.HandleMessage(lParam) ?? false;
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private void StartEyedropperFromHotkey()
    {
        if (_viewModel?.ColorPicker is { IsPicking: false } picker)
        {
            picker.PickCommand.Execute(null);
        }
    }

    private void ShowTrayMenu()
    {
        EnsureTrayMenu();
        ActivateForInput();

        _trayMenu!.Placement = PlacementMode.MousePoint;
        _trayMenu.IsOpen = true;
    }

    private void EnsureTrayMenu()
    {
        if (_trayMenu is null)
        {
            _trayMenu = new ContextMenu();

            var show = new MenuItem { Header = "Показать" };
            show.Click += (_, _) =>
            {
                _isPinned = true;
                Expand();
            };

            _autostartItem = new MenuItem
            {
                Header = "Запускать с Windows",
                IsCheckable = true,
            };
            _autostartItem.Click += (_, _) => StartupRegistry.SetEnabled(_autostartItem.IsChecked);

            var restart = new MenuItem { Header = "Перезапустить" };
            restart.Click += (_, _) => RestartApplication();

            var exit = new MenuItem { Header = "Выход" };
            exit.Click += (_, _) => System.Windows.Application.Current.Shutdown();

            var dockMenu = new MenuItem { Header = "Расположение" };
            foreach (var (dock, title) in new (IslandDock Dock, string Title)[]
            {
                (IslandDock.Top, "Сверху"),
                (IslandDock.Bottom, "Снизу (над панелью)"),
            })
            {
                var item = new MenuItem { Header = title, IsCheckable = true };
                item.Click += (_, _) => SetDock(dock);
                dockMenu.Items.Add(item);
                _dockItems[dock] = item;
            }

            _trayMenu.Items.Add(show);
            _trayMenu.Items.Add(dockMenu);
            _trayMenu.Items.Add(_autostartItem);
            _trayMenu.Items.Add(new Separator());
            _trayMenu.Items.Add(restart);
            _trayMenu.Items.Add(exit);
        }

        _autostartItem!.IsChecked = StartupRegistry.IsEnabled();
        foreach (var (dock, item) in _dockItems)
        {
            item.IsChecked = dock == _dock;
        }
    }

    private void OnRestartClick(object sender, RoutedEventArgs e) => RestartApplication();

    private static void RestartApplication() => AppLifecycle.Restart();

    public void ActivateForInput()
    {
        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.SetForegroundWindow(handle);
        Activate();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyDock();
    }

    private void OnDockClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string tag && Enum.TryParse<IslandDock>(tag, out var dock))
        {
            SetDock(dock);
        }
    }

    private void OnIslandMenuOpening(object sender, RoutedEventArgs e)
    {
        if (Island.ContextMenu is not ContextMenu menu)
        {
            return;
        }

        foreach (var top in menu.Items.OfType<MenuItem>())
        {
            foreach (var sub in top.Items.OfType<MenuItem>())
            {
                if (sub.Tag is string tag && Enum.TryParse<IslandDock>(tag, out var dock))
                {
                    sub.IsChecked = dock == _dock;
                }
            }
        }
    }

    private void SetDock(IslandDock dock)
    {
        if (_dock == dock)
        {
            ApplyDock();
            return;
        }

        _dock = dock;
        _settings.Dock = dock;
        BorderEx.Dock = dock;
        SettingsStore.Save();
        ApplyDock();
    }

    private void ApplyDock()
    {
        var bottom = _dock == IslandDock.Bottom;

        // Keep the rail glued to the docked screen edge: at the bottom for the
        // bottom dock, at the top otherwise. This avoids the rail jumping over
        // the panel when the island expands.
        Grid.SetRow(SectionRail, bottom ? 1 : 0);
        Grid.SetRow(ContentSurface, bottom ? 0 : 1);
        ExpandedContent.RowDefinitions[0].Height = bottom
            ? new GridLength(1, GridUnitType.Star)
            : GridLength.Auto;
        ExpandedContent.RowDefinitions[1].Height = bottom
            ? GridLength.Auto
            : new GridLength(1, GridUnitType.Star);
        SectionRail.Margin = bottom
            ? new Thickness(0, 6, 0, 0)
            : new Thickness(0, 0, 0, 6);

        Island.HorizontalAlignment = HorizontalAlignment.Center;
        Island.VerticalAlignment = bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;

        BorderEx.Apply(Island, _isExpanded ? ExpandedRadius : CollapsedRadius);
        PositionWindow();
    }

    private void PositionWindow()
    {
        var work = SystemParameters.WorkArea;

        var (left, top) = _dock switch
        {
            IslandDock.Bottom => (work.Left + ((work.Width - Width) / 2), work.Bottom - Height),
            _ => (work.Left + ((work.Width - Width) / 2), work.Top),
        };

        Left = left;
        Top = top;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Music.PropertyChanged -= OnMusicPropertyChanged;
        }

        _viewModel = DataContext as MainViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Music.PropertyChanged += OnMusicPropertyChanged;
        }

        UpdateTrayToolTip();
    }

    private void OnMusicPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MusicViewModel.Track) or nameof(MusicViewModel.IsMuted))
        {
            UpdateTrayToolTip();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (e.PropertyName == nameof(MainViewModel.ShowMiniPlayer))
        {
            if (!_isExpanded)
            {
                CollapsedContent.Width = CollapsedTargetWidth();
                AnimateIsland(CollapsedTargetWidth(), CollapsedHeight, CollapsedRadius, MorphSpring, MorphMs);
            }

            return;
        }

        if (e.PropertyName != nameof(MainViewModel.SelectedSection))
        {
            return;
        }

        var section = _viewModel.SelectedSection;

        if (!_isExpanded)
        {
            SwitchSection(section, animated: false);
            Expand();

            return;
        }

        SwitchSection(section, animated: true);
        AnimateToCurrent(isMorph: true);
    }

    private FrameworkElement SectionElement(IslandSection section) => section switch
    {
        IslandSection.Clipboard => ClipboardSection,
        IslandSection.Screenshots => ScreenshotsSection,
        IslandSection.Translator => TranslatorSection,
        IslandSection.ColorPicker => ColorPickerSection,
        _ => MusicSection,
    };

    private void SwitchSection(IslandSection section, bool animated)
    {
        var target = SectionElement(section);
        var duration = animated ? 110.0 : 0.0;

        foreach (var element in _sections)
        {
            if (ReferenceEquals(element, target))
            {
                Panel.SetZIndex(element, 2);
                element.Visibility = Visibility.Visible;

                var scale = (ScaleTransform)element.RenderTransform;

                if (animated)
                {
                    Fade(element, 0, 1, duration, EaseOut);
                    AnimateUnfold(scale);
                }
                else
                {
                    element.BeginAnimation(OpacityProperty, null);
                    element.Opacity = 1;
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    scale.ScaleX = 1;
                    scale.ScaleY = 1;
                }
            }
            else if (element.Visibility == Visibility.Visible)
            {
                Panel.SetZIndex(element, 1);

                if (animated)
                {
                    Fade(element, element.Opacity, 0, duration, EaseOut,
                        completed: () => element.Visibility = Visibility.Hidden);
                }
                else
                {
                    element.BeginAnimation(OpacityProperty, null);
                    element.Opacity = 0;
                    element.Visibility = Visibility.Hidden;
                }
            }
        }
    }

    /// <summary>Grows the content from the middle when a section becomes active.</summary>
    private static void AnimateUnfold(ScaleTransform scale)
    {
        const double from = 0.82;
        var duration = TimeSpan.FromMilliseconds(UnfoldMs);

        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(from, 1, duration) { EasingFunction = UnfoldSpring });
        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(from, 1, duration) { EasingFunction = UnfoldSpring });
    }

    private void EnterPicking()
    {
        _isPinned = true;
        _hoverTimer.Stop();
        Hide();
    }

    private void ExitPicking()
    {
        Show();

        if (_isExpanded)
        {
            _outsideHoverTicks = 0;
            _hoverTimer.Start();
        }
    }

    private void OnIslandMouseEnter(object sender, MouseEventArgs e)
    {
        Expand();
    }

    private void OnIslandMouseLeave(object sender, MouseEventArgs e)
    {
        // Collapse is handled by the hover watcher, which tolerates the island
        // resizing under a stationary cursor (no enter/leave flapping).
    }

    private void OnHoverTimerTick(object? sender, EventArgs e)
    {
        if (_isPinned || !_isExpanded)
        {
            return;
        }

        if (IsCursorWithinHoverZone())
        {
            _outsideHoverTicks = 0;
            return;
        }

        _outsideHoverTicks++;
        if (_outsideHoverTicks >= 3)
        {
            _outsideHoverTicks = 0;
            Collapse();
        }
    }

    private bool IsCursorWithinHoverZone()
    {
        // WPF's Mouse.GetPosition only tracks positions from received mouse
        // messages, so it goes stale over the transparent parts of the window.
        // Use the real cursor position instead.
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return true;
        }

        var topLeft = Island.PointToScreen(new Point(0, 0));
        const double padding = 18;

        return cursor.X >= topLeft.X - padding
            && cursor.Y >= topLeft.Y - padding
            && cursor.X <= topLeft.X + Island.ActualWidth + padding
            && cursor.Y <= topLeft.Y + Island.ActualHeight + padding;
    }

    private void OnIslandClick(object sender, MouseButtonEventArgs e)
    {
        if (!_isExpanded)
        {
            _isPinned = true;
            Expand();
        }
    }

    private void OnIslandRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Popups need an active window for mouse capture, so activate first and
        // open the menu explicitly (the automatic handler is unreliable here).
        ActivateForInput();

        if (Island.ContextMenu is { IsOpen: false } menu)
        {
            e.Handled = true;
            menu.PlacementTarget = Island;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
        }
    }

    private void OnExpandClick(object sender, RoutedEventArgs e)
    {
        _isPinned = true;
        Expand();
    }

    private void OnCollapseClick(object sender, RoutedEventArgs e)
    {
        _isPinned = false;
        Collapse();
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }

    public void ShowSectionForDebug(IslandSection section)
    {
        _isPinned = true;
        if (_viewModel is not null)
        {
            _viewModel.SelectedSection = section;
        }

        Expand();
    }

    private double CollapsedTargetWidth()
        => _viewModel?.ShowMiniPlayer == true ? MiniWidth : RailWidth;

    private void Expand()
    {
        if (_isExpanded)
        {
            AnimateToCurrent(isMorph: false);
            return;
        }

        MarkTransition("Expand");
        _isExpanded = true;
        _outsideHoverTicks = 0;
        _hoverTimer.Start();
        CollapsedContent.Width = CollapsedTargetWidth();
        ExpandedContent.Visibility = Visibility.Visible;
        ExpandedContent.BeginAnimation(OpacityProperty, null);
        ExpandedContent.Opacity = 1;
        ContentSurface.BeginAnimation(OpacityProperty, null);
        ContentSurface.Opacity = 1;
        CollapsedContent.Visibility = Visibility.Visible;
        var (width, height) = CurrentTargetSize();
        SetContentWidth(width);
        ExpandedContent.BeginAnimation(HeightProperty, null);
        ExpandedContent.Height = CollapsedHeight - 12;
        AnimateIsland(width, height, ExpandedRadius, ExpandSpring, ExpandMs);
        AnimateContentHeight(height, ExpandSpring, ExpandMs);

        Fade(CollapsedContent, 1, 0, 150, EaseIn, completed: () =>
        {
            CollapsedContent.Visibility = Visibility.Hidden;
            CollapsedEqualizer.Pause();
        });
    }

    private void Collapse()
    {
        if (!_isExpanded)
        {
            return;
        }

        MarkTransition("Collapse");
        _isExpanded = false;
        _hoverTimer.Stop();
        _outsideHoverTicks = 0;
        if (CollapsedContent.Visibility != Visibility.Visible)
        {
            CollapsedContent.Visibility = Visibility.Visible;
        }

        CollapsedContent.Width = CollapsedTargetWidth();
        CollapsedEqualizer.Resume();

        AnimateIsland(CollapsedTargetWidth(), CollapsedHeight, CollapsedRadius, CollapseEase, CollapseMs);
        AnimateContentHeight(CollapsedHeight, CollapseEase, CollapseMs);

        Fade(CollapsedContent, 0, 1, 220, EaseOut, beginMs: 110);
        Fade(ExpandedContent, 1, 0, 150, EaseIn, completed: () => ExpandedContent.Visibility = Visibility.Hidden);
    }

    private (double Width, double Height) CurrentTargetSize()
    {
        return _viewModel?.SelectedSection switch
        {
            IslandSection.Music => (DetailWidth, MusicHeight),
            IslandSection.Screenshots => (DetailWidth, ScreenshotsHeight),
            IslandSection.Translator => (DetailWidth, TranslatorHeight),
            IslandSection.ColorPicker => (ColorPickerWidth, ColorPickerHeight),
            _ => (DetailWidth, ClipboardHeight),
        };
    }

    private void AnimateToCurrent(bool isMorph)
    {
        MarkTransition(isMorph ? "Morph" : "Expand");
        var (width, height) = CurrentTargetSize();
        SetContentWidth(width);

        var easing = isMorph ? MorphSpring : ExpandSpring;
        var durationMs = isMorph ? MorphMs : ExpandMs;

        AnimateIsland(width, height, ExpandedRadius, easing, durationMs);
        AnimateContentHeight(height, easing, durationMs);
    }

    private void SetContentWidth(double width)
    {
        ExpandedContent.Width = width - 16;
    }

    private void AnimateContentHeight(double height, IEasingFunction easing, double durationMs)
    {
        var duration = TimeSpan.FromMilliseconds(durationMs);

        ExpandedContent.BeginAnimation(
            HeightProperty,
            new DoubleAnimation(height - 12, duration) { EasingFunction = easing });
    }

    private void AnimateIsland(double width, double height, double radius, IEasingFunction easing, double durationMs)
    {
        var duration = TimeSpan.FromMilliseconds(durationMs);

        Island.BeginAnimation(WidthProperty, new DoubleAnimation(width, duration) { EasingFunction = easing });
        Island.BeginAnimation(HeightProperty, new DoubleAnimation(height, duration) { EasingFunction = easing });
        Island.BeginAnimation(
            BorderEx.AnimatedRadiusProperty,
            new DoubleAnimation(radius, duration) { EasingFunction = easing });
    }

    private static void Fade(
        UIElement element,
        double from,
        double to,
        double durationMs,
        IEasingFunction easing,
        double beginMs = 0,
        Action? completed = null)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = easing,
            BeginTime = TimeSpan.FromMilliseconds(beginMs),
        };

        if (completed is not null)
        {
            animation.Completed += (_, _) => completed();
        }

        element.BeginAnimation(OpacityProperty, animation);
    }


}
