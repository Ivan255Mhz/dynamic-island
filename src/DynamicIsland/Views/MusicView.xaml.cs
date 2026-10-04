using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DynamicIsland.ViewModels;

namespace DynamicIsland.Views;

public partial class MusicView : UserControl
{
    private readonly DispatcherTimer _volumeWatch;

    public MusicView()
    {
        InitializeComponent();

        _volumeWatch = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(700),
        };
        _volumeWatch.Tick += (_, _) => (DataContext as MusicViewModel)?.RefreshVolume();

        IsVisibleChanged += OnIsVisibleChanged;
        Unloaded += (_, _) => _volumeWatch.Stop();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            (DataContext as MusicViewModel)?.RefreshVolume();
            _volumeWatch.Start();
        }
        else
        {
            _volumeWatch.Stop();
        }
    }

    private void OnVolumeWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not MusicViewModel viewModel)
        {
            return;
        }

        viewModel.Volume = Math.Clamp(viewModel.Volume + (e.Delta > 0 ? 0.05 : -0.05), 0, 1);
        e.Handled = true;
    }
}
