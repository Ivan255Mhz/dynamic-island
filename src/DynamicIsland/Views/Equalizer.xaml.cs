using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland.Views;

/// <summary>
/// A small monochrome equalizer. Bars animate while <see cref="IsPlaying"/> is
/// true and rest flat otherwise.
/// </summary>
public partial class Equalizer : UserControl
{
    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
        nameof(IsPlaying),
        typeof(bool),
        typeof(Equalizer),
        new PropertyMetadata(false, OnIsPlayingChanged));

    private readonly (ScaleTransform Transform, double Duration)[] _bars;

    public Equalizer()
    {
        InitializeComponent();

        _bars = new[]
        {
            (Bar0Scale, 0.55),
            (Bar1Scale, 0.78),
            (Bar2Scale, 0.44),
            (Bar3Scale, 0.66),
        };

        Apply(false);
    }

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    private bool _paused;

    public void Pause()
    {
        _paused = true;
        Apply(false);
    }

    public void Resume()
    {
        _paused = false;
        Apply(IsPlaying);
    }

    private static void OnIsPlayingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((Equalizer)d).Apply((bool)e.NewValue);

    private void Apply(bool playing)
    {
        if (_paused || Environment.GetEnvironmentVariable("DI_NOEQ") == "1")
        {
            playing = false;
        }

        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };

        for (var i = 0; i < _bars.Length; i++)
        {
            var (transform, duration) = _bars[i];

            if (!playing)
            {
                transform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                transform.ScaleY = 0.3;
                continue;
            }

            var animation = new DoubleAnimation
            {
                From = 0.25,
                To = 1.0,
                Duration = TimeSpan.FromSeconds(duration),
                BeginTime = TimeSpan.FromSeconds(i * 0.13),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = ease,
            };

            transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }
    }
}
