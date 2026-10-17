using System;
using System.IO;
using System.Text.Json;
using WritingTools.Models;

namespace WritingTools.Services;

public class SettingsService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WritingTools"
    );

    private static readonly string SettingsFile = Path.Combine(ConfigDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public AppSettings Settings { get; private set; } = new();

    public SettingsService()
    {
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded != null)
                {
                    Settings = loaded;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
        }

        Settings = new AppSettings();
    }

    public void Save()
    {
        try
        {
            if (!Directory.Exists(ConfigDir))
            {
                Directory.CreateDirectory(ConfigDir);
            }

            var json = JsonSerializer.Serialize(Settings, JsonOptions);
            var tempFile = Path.Combine(ConfigDir, "settings.json.tmp");
            File.WriteAllText(tempFile, json);
            File.Move(tempFile, SettingsFile, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    public void AddHistory(HistoryItem item)
    {
        Settings.RecentHistory.Insert(0, item);
        while (Settings.RecentHistory.Count > 15)
        {
            Settings.RecentHistory.RemoveAt(Settings.RecentHistory.Count - 1);
        }
        Save();
    }

    public void ClearHistory()
    {
        Settings.RecentHistory.Clear();
        Save();
    }
}
