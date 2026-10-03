using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DynamicIsland.Models;

namespace DynamicIsland.Infrastructure;

public sealed class AppSettings
{
    public IslandDock Dock { get; set; } = IslandDock.Top;
}

/// <summary>Persists user preferences as JSON under %APPDATA%\DynamicIsland.</summary>
internal static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly string FolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DynamicIsland");

    private static readonly string FilePath = Path.Combine(FolderPath, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            }
        }
        catch
        {
            // Fall back to defaults on any read/parse error.
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
        }
        catch
        {
            // Preferences are best-effort.
        }
    }
}
