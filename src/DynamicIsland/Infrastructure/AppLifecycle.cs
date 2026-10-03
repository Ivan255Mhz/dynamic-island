using System.Diagnostics;
using System.Windows;
using Application = System.Windows.Application;

namespace DynamicIsland.Infrastructure;

/// <summary>
/// Single-instance guard and self-restart helper used by the tray menu.
/// </summary>
internal static class AppLifecycle
{
    private const string MutexName = "DynamicIsland.SingleInstance.v1";

    private static Mutex? _mutex;

    public static bool TryAcquireSingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew)
        {
            return true;
        }

        _mutex.Dispose();
        _mutex = null;
        return false;
    }

    public static void Restart()
    {
        ReleaseMutex();

        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                });
            }
        }
        catch
        {
            // If the relaunch fails the current instance keeps running.
            return;
        }

        Application.Current?.Shutdown();
    }

    private static void ReleaseMutex()
    {
        if (_mutex is null)
        {
            return;
        }

        try
        {
            _mutex.ReleaseMutex();
        }
        catch
        {
            // Not owned by this thread; ignore.
        }

        _mutex.Dispose();
        _mutex = null;
    }
}
