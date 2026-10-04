using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using DynamicIsland.Infrastructure;
using DynamicIsland.Services;
using DynamicIsland.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Application = System.Windows.Application;

namespace DynamicIsland;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!AppLifecycle.TryAcquireSingleInstance())
        {
            Shutdown();
            return;
        }

        EnableBindingTraceIfRequested();

        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        var window = _services.GetRequiredService<MainWindow>();
        window.DataContext = _services.GetRequiredService<MainViewModel>();

        StartBackgroundServices();

        window.Show();

        OpenDebugSectionIfRequested(window);
    }

    private static void OpenDebugSectionIfRequested(MainWindow window)
    {
        var requested = Environment.GetEnvironmentVariable("DI_OPEN");
        if (Enum.TryParse<IslandSection>(requested, ignoreCase: true, out var section))
        {
            window.Dispatcher.BeginInvoke(
                () => window.ShowSectionForDebug(section),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private static void EnableBindingTraceIfRequested()
    {
        if (Environment.GetEnvironmentVariable("DI_TRACE") != "1")
        {
            return;
        }

        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "di_bindings.log");
        var writer = new System.IO.StreamWriter(path, append: false) { AutoFlush = true };
        var listener = new TextWriterTraceListener(writer) { TraceOutputOptions = TraceOptions.None };

        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<HttpClient>();

        services.AddSingleton<IMediaService, WindowsMediaService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IScreenshotService, ScreenshotService>();
        services.AddSingleton<ITranslationService, GoogleTranslationService>();
        services.AddSingleton<IScreenColorPicker, ScreenColorPickerService>();

        services.AddSingleton<MusicViewModel>();
        services.AddSingleton<ClipboardViewModel>();
        services.AddSingleton<ScreenshotsViewModel>();
        services.AddSingleton<TranslatorViewModel>();
        services.AddSingleton<ColorPickerViewModel>();
        services.AddSingleton<MainViewModel>();

        services.AddSingleton<MainWindow>();
    }

    private void StartBackgroundServices()
    {
        if (_services is null)
        {
            return;
        }

        _ = _services.GetRequiredService<IMediaService>().StartAsync();
        _services.GetRequiredService<IClipboardService>().Start();
        _services.GetRequiredService<IScreenshotService>().Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_services is not null)
        {
            _services.GetRequiredService<IClipboardService>().Dispose();
            _services.GetRequiredService<IScreenshotService>().Dispose();
            _services.Dispose();
        }

        base.OnExit(e);
    }
}
