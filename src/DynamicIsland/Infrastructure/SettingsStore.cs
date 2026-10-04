using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DynamicIsland.Models;

namespace DynamicIsland.Infrastructure;

public sealed class AppSettings
{
    public IslandDock Dock { get; set; } = IslandDock.Top;

    public string LastColor { get; set; } = "#8B5CF6";
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

    private static AppSettings? _current;

    /// <summary>Shared settings instance so different parts do not clobber each other.</summary>
    public static AppSettings Current => _current ??= Load();

    public static void Save()
    {
        if (_current is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(FolderPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_current, Options));
        }
        catch
        {
            // Preferences are best-effort.
        }
    }

    private static AppSettings Load()
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
}
