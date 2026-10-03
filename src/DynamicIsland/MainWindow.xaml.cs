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
using DynamicIsland.ViewModels;

namespace DynamicIsland;

public partial class MainWindow : Window
{
    private const double RailWidth = 210;
    private const double MiniWidth = 252;
    private const double CollapsedHeight = 40;
    private const double CollapsedRadius = 20;

    private const double DetailWidth = 380;
    private const double MusicHeight = 126;
    private const double ScreenshotsHeight = 136;
    private const double ClipboardHeight = 238;
    private const double TranslatorHeight = 260;
    private const double ExpandedRadius = 22;

    private const double ExpandMs = 460;
    private const double CollapseMs = 320;
    private const double MorphMs = 440;
    private const double ContentFadeMs = 260;

    private static readonly SpringEase ExpandSpring = new() { Damping = 1.0, Frequency = 10.5 };
    private static readonly SpringEase MorphSpring = new() { Damping = 1.0, Frequency = 11.5 };
    private static readonly CubicEase EaseOut = new() { EasingMode = EasingMode.EaseOut };
    private static readonly CubicEase CollapseEase = new() { EasingMode = EasingMode.EaseInOut };
    private static readonly QuadraticEase EaseIn = new() { EasingMode = EasingMode.EaseIn };

    private readonly DispatcherTimer _collapseTimer;

    private MainViewModel? _viewModel;
    private bool _isExpanded;
    private bool _isPinned;
    private TrayIcon? _tray;
    private ContextMenu? _trayMenu;
    private MenuItem? _autostartItem;

    public MainWindow()
    {
        InitializeComponent();

        if (Environment.GetEnvironmentVariable("DI_NOTRANS") == "1")
        {
            AllowsTransparency = false;
            Background = Brushes.Black;
        }

        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _collapseTimer.Tick += OnCollapseTimerTick;

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
        var style = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
        style |= NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLong(handle, NativeMethods.GWL_EXSTYLE, style);

        HwndSource.FromHwnd(handle)?.AddHook(WndProc);

        _tray = new TrayIcon(handle, "Dynamic Island");
        _tray.MenuRequested += (_, _) => ShowTrayMenu();
        _tray.RestartRequested += (_, _) => RestartApplication();

        Closed += (_, _) =>
        {
            _tray?.Dispose();
            _tray = null;
        };
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(NativeMethods.MA_NOACTIVATE);
        }

        if (msg == TrayIcon.CallbackMessage)
        {
            handled = _tray?.HandleMessage(lParam) ?? false;
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
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

            _trayMenu.Items.Add(show);
            _trayMenu.Items.Add(_autostartItem);
            _trayMenu.Items.Add(new Separator());
            _trayMenu.Items.Add(restart);
            _trayMenu.Items.Add(exit);
        }

        _autostartItem!.IsChecked = StartupRegistry.IsEnabled();
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
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
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
        _ => MusicSection,
    };

    private void SwitchSection(IslandSection section, bool animated)
    {
        var target = SectionElement(section);
        var duration = animated ? 180.0 : 0.0;

        foreach (var element in new FrameworkElement[] { MusicSection, ClipboardSection, ScreenshotsSection, TranslatorSection })
        {
            if (ReferenceEquals(element, target))
            {
                Panel.SetZIndex(element, 2);
                element.Visibility = Visibility.Visible;

                if (animated)
                {
                    Fade(element, 0, 1, duration, EaseOut);
                }
                else
                {
                    element.BeginAnimation(OpacityProperty, null);
                    element.Opacity = 1;
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

    private void OnIslandMouseEnter(object sender, MouseEventArgs e)
    {
        _collapseTimer.Stop();
        Expand();
    }

    private void OnIslandMouseLeave(object sender, MouseEventArgs e)
    {
        if (_isPinned)
        {
            return;
        }

        _collapseTimer.Stop();
        _collapseTimer.Start();
    }

    private void OnCollapseTimerTick(object? sender, EventArgs e)
    {
        _collapseTimer.Stop();
        if (!_isPinned && !IsMouseOver)
        {
            Collapse();
        }
    }

    private void OnIslandClick(object sender, MouseButtonEventArgs e)
    {
        if (!_isExpanded)
        {
            _isPinned = true;
            Expand();
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
        Fade(ContentSurface, 0.45, 1, 220, EaseOut, beginMs: 40);

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
