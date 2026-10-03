using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Interop;
using DynamicIsland.Models;

namespace DynamicIsland.Services;

/// <summary>
/// Polls the Windows clipboard on the UI thread, keeps a bounded history and
/// raises <see cref="ClipboardChanged"/> whenever new content appears.
/// </summary>
public sealed class ClipboardService : IClipboardService
{
    private const int MaxHistory = 12;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(400);

    private readonly DispatcherTimer _timer;
    private readonly List<ClipboardEntry> _history = new();

    private uint _lastSequenceNumber;
    private ClipboardEntry _current = new(ClipboardContentKind.Empty, null, null, DateTimeOffset.Now);

    public ClipboardService()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = PollInterval,
        };
        _timer.Tick += OnTick;
    }

    public ClipboardEntry Current => _current;

    public IReadOnlyList<ClipboardEntry> History => _history;

    public event EventHandler<ClipboardEntry>? ClipboardChanged;

    public void Start()
    {
        Poll();
        _timer.Start();
    }

    public void SetText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        RunWithRetry(() => Clipboard.SetText(text));
    }

    private void Poll()
    {
        try
        {
            Infrastructure.Diag.ClipboardPolls++;

            var sequence = NativeMethods.GetClipboardSequenceNumber();
            if (sequence == _lastSequenceNumber)
            {
                return;
            }

            _lastSequenceNumber = sequence;
            Infrastructure.Diag.ClipboardReads++;

            var data = Clipboard.GetDataObject();
            if (data is null)
            {
                return;
            }

            if (data.GetDataPresent(DataFormats.Bitmap))
            {
                CaptureImage();
            }
            else if (data.GetDataPresent(DataFormats.UnicodeText))
            {
                CaptureText();
            }
        }
        catch (COMException)
        {
            // The clipboard is momentarily locked by another process; try again next tick.
        }
        catch
        {
            // Ignore malformed clipboard payloads.
        }
    }

    private void OnTick(object? sender, EventArgs e) => Poll();

    private void CaptureText()
    {
        var text = RunWithRetry(Clipboard.GetText);
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var kind = IsLink(text) ? ClipboardContentKind.Link : ClipboardContentKind.Text;
        Add(new ClipboardEntry(kind, text, null, DateTimeOffset.Now, text.GetHashCode()));
    }

    private static bool IsLink(string text)
    {
        var trimmed = text.Trim();
        return !trimmed.Contains('\n') &&
               (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("www.", StringComparison.OrdinalIgnoreCase));
    }

    private void CaptureImage()
    {
        var image = RunWithRetry(Clipboard.GetImage);
        if (image is null)
        {
            return;
        }

        var signature = HashBitmap(image);

        image.Freeze();
        Add(new ClipboardEntry(ClipboardContentKind.Image, null, image, DateTimeOffset.Now, signature));
    }

    private void Add(ClipboardEntry entry)
    {
        if (entry.Signature != 0 &&
            _current.Kind == entry.Kind &&
            _current.Signature == entry.Signature)
        {
            // Identical to what is already on top; nothing changed.
            return;
        }

        Infrastructure.Diag.ClipboardChanges++;

        // Keep only unique entries: drop an earlier occurrence and move it to the top.
        if (entry.Signature != 0)
        {
            var existing = _history.FindIndex(e => e.Kind == entry.Kind && e.Signature == entry.Signature);
            if (existing >= 0)
            {
                _history.RemoveAt(existing);
            }
        }

        _current = entry;
        _history.Insert(0, entry);
        if (_history.Count > MaxHistory)
        {
            _history.RemoveRange(MaxHistory, _history.Count - MaxHistory);
        }

        ClipboardChanged?.Invoke(this, entry);
    }

    private static int HashBitmap(BitmapSource source)
    {
        try
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var stride = ((converted.PixelWidth * 4) + 3) / 4 * 4;
            var buffer = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(buffer, stride, 0);

            var hash = new HashCode();
            hash.AddBytes(buffer);
            return hash.ToHashCode();
        }
        catch
        {
            return Guid.NewGuid().GetHashCode();
        }
    }

    private static T? RunWithRetry<T>(Func<T> action, int attempts = 5)
    {
        for (var i = 0; i < attempts; i++)
        {
            try
            {
                return action();
            }
            catch (COMException) when (i < attempts - 1)
            {
                Thread.Sleep(20);
            }
        }

        return default;
    }

    private static void RunWithRetry(Action action, int attempts = 5)
    {
        RunWithRetry<bool>(() =>
        {
            action();
            return true;
        }, attempts);
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
