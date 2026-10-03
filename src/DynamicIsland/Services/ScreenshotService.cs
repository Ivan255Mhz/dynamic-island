using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Interop;
using DynamicIsland.Models;

namespace DynamicIsland.Services;

/// <summary>
/// Watches the Windows Screenshots folder and exposes the most recent captures
/// as decoded thumbnails. The folder is resolved through the shell known-folder
/// API so localized Windows installs are handled correctly.
/// </summary>
public sealed class ScreenshotService : IScreenshotService
{
    private const int MaxItems = 12;
    private const int ThumbnailWidth = 220;

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _debounce;
    private readonly List<ScreenshotItem> _items = new();

    private FileSystemWatcher? _watcher;
    private string? _folder;

    public ScreenshotService()
    {
        _dispatcher = Application.Current.Dispatcher;
        _debounce = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(350),
        };
        _debounce.Tick += OnDebounceTick;
    }

    public IReadOnlyList<ScreenshotItem> Items => _items;

    public string? FolderPath => _folder;

    public event EventHandler<IReadOnlyList<ScreenshotItem>>? ItemsChanged;

    public void Start()
    {
        _folder = ResolveFolder();
        if (_folder is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_folder);

            _watcher = new FileSystemWatcher(_folder)
            {
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.CreationTime,
                Filter = "*.png",
                EnableRaisingEvents = true,
            };
            _watcher.Created += OnFolderChanged;
            _watcher.Changed += OnFolderChanged;
            _watcher.Deleted += OnFolderChanged;
            _watcher.Renamed += OnFolderRenamed;
        }
        catch
        {
            _watcher = null;
        }

        Refresh();
    }

    private static string? ResolveFolder()
    {
        var known = NativeMethods.TryGetScreenshotsFolder();
        if (!string.IsNullOrWhiteSpace(known) && Directory.Exists(known))
        {
            return known;
        }

        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(pictures))
        {
            return null;
        }

        foreach (var name in new[] { "Screenshots", "Снимки экрана" })
        {
            var candidate = Path.Combine(pictures, name);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return known ?? Path.Combine(pictures, "Screenshots");
    }

    private void OnFolderChanged(object sender, FileSystemEventArgs e) => ScheduleRefresh();

    private void OnFolderRenamed(object sender, RenamedEventArgs e) => ScheduleRefresh();

    private void ScheduleRefresh()
    {
        if (_dispatcher.CheckAccess())
        {
            _debounce.Stop();
            _debounce.Start();
        }
        else
        {
            _dispatcher.Invoke(() =>
            {
                _debounce.Stop();
                _debounce.Start();
            });
        }
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounce.Stop();
        Refresh();
    }

    private void Refresh()
    {
        Infrastructure.Diag.ScreenshotRefreshes++;

        if (_folder is null || !Directory.Exists(_folder))
        {
            return;
        }

        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(_folder, "*.png")
                .Select(path => new FileInfo(path))
                .Where(info => info.Exists)
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .Take(MaxItems)
                .Select(info => info.FullName)
                .ToList();
        }
        catch
        {
            return;
        }

        var result = new List<ScreenshotItem>(files.Count);
        foreach (var file in files)
        {
            var thumbnail = LoadThumbnail(file);
            result.Add(new ScreenshotItem(
                file,
                File.GetLastWriteTime(file),
                thumbnail));
        }

        _items.Clear();
        _items.AddRange(result);
        ItemsChanged?.Invoke(this, _items);
    }

    private static ImageSource? LoadThumbnail(string path)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = ThumbnailWidth;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (IOException)
            {
                Thread.Sleep(80);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    public void Dispose()
    {
        _debounce.Stop();
        _debounce.Tick -= OnDebounceTick;

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFolderChanged;
            _watcher.Changed -= OnFolderChanged;
            _watcher.Deleted -= OnFolderChanged;
            _watcher.Renamed -= OnFolderRenamed;
            _watcher.Dispose();
            _watcher = null;
        }
    }
}
