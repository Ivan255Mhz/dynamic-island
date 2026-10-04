using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Infrastructure;
using DynamicIsland.Services;

namespace DynamicIsland.ViewModels;

public sealed partial class ColorPickerViewModel : ObservableObject
{
    private readonly IClipboardService _clipboard;
    private readonly IScreenColorPicker _screenPicker;
    private readonly DispatcherTimer _saveTimer;
    private bool _syncing;

    [ObservableProperty]
    private Color _color = Colors.Black;

    [ObservableProperty]
    private double _hue;

    [ObservableProperty]
    private double _saturation = 1;

    [ObservableProperty]
    private double _value = 1;

    [ObservableProperty]
    private string _hexText = string.Empty;

    [ObservableProperty]
    private string _rgbText = string.Empty;

    [ObservableProperty]
    private string _hslText = string.Empty;

    [ObservableProperty]
    private string _cssText = string.Empty;

    [ObservableProperty]
    private string _activeFormat = "HEX";

    [ObservableProperty]
    private bool _isPicking;

    [ObservableProperty]
    private Brush _swatchBrush = Brushes.Black;

    public ColorPickerViewModel(IClipboardService clipboard, IScreenColorPicker screenPicker)
    {
        _clipboard = clipboard;
        _screenPicker = screenPicker;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SettingsStore.Current.LastColor = ColorMath.ToHex(Color);
            SettingsStore.Save();
        };

        var initial = ColorMath.TryParseHex(SettingsStore.Current.LastColor, out var saved)
            ? saved
            : Color.FromRgb(0x8B, 0x5C, 0xF6);

        ApplyColor(initial, updateHsv: true);
    }

    partial void OnHueChanged(double value) => SyncFromHsv();

    partial void OnSaturationChanged(double value) => SyncFromHsv();

    partial void OnValueChanged(double value) => SyncFromHsv();

    [RelayCommand]
    private void ApplyHex()
    {
        if (ColorMath.TryParseHex(HexText, out var color))
        {
            ApplyColor(color, updateHsv: true);
        }
    }

    [RelayCommand]
    private void CopyHex() => Copy(HexText, "HEX");

    [RelayCommand]
    private void CopyRgb() => Copy(RgbText, "RGB");

    [RelayCommand]
    private void CopyHsl() => Copy(HslText, "HSL");

    [RelayCommand]
    private void CopyCss() => Copy(CssText, "CSS");

    [RelayCommand]
    private async Task PickAsync()
    {
        IsPicking = true;
        try
        {
            var picked = await _screenPicker.PickAsync();
            if (picked is { } color)
            {
                ApplyColor(color, updateHsv: true);
            }
        }
        finally
        {
            IsPicking = false;
        }
    }

    private void SyncFromHsv()
    {
        if (_syncing)
        {
            return;
        }

        ApplyColor(ColorMath.FromHsv(Hue, Saturation, Value), updateHsv: false);
    }

    private void ApplyColor(Color color, bool updateHsv)
    {
        _syncing = true;
        if (updateHsv)
        {
            var (hue, saturation, value) = ColorMath.ToHsv(color);
            Hue = hue;
            Saturation = saturation;
            Value = value;
        }

        Color = color;
        _syncing = false;

        HexText = ColorMath.ToHex(color);
        RgbText = ColorMath.ToRgbText(color);
        HslText = ColorMath.ToHslText(color);
        CssText = ColorMath.ToCssText(color);
        SwatchBrush = new SolidColorBrush(color);

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void Copy(string text, string format)
    {
        _clipboard.SetText(text);
        ActiveFormat = format;
    }
}
