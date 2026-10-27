using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HoverView;

public class AppSettings
{
    public double WindowWidth { get; set; } = 400;
    public double WindowHeight { get; set; } = 300;
    public bool AutoStart { get; set; } = false;

    public bool AutoSizeToContent { get; set; } = false;

    public bool UseMinimalFrame { get; set; } = false;

    public int MaxCacheSize { get; set; } = 50;          // Максимум изображений в кэше
    public int HoverDelayMs { get; set; } = 300;         // Задержка появления (мс)
    public int HideDelayMs { get; set; } = 500;

    public List<string> ImageFormats { get; set; } = new() 
    { 
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" 
    };
    public List<string> VideoFormats { get; set; } = new() 
    { 
        ".mp4", ".avi", ".mkv", ".mov", ".wmv" 
    };
    public List<string> AudioFormats { get; set; } = new() 
    { 
        ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a" 
    };
    public List<string> TextFormats { get; set; } = new() 
    { 
        ".txt", ".log", ".json", ".xml", ".csv", ".md", ".ini", ".config", 
        ".cs", ".cpp", ".h", ".py", ".js", ".html", ".css" 
    };

    public int MaxFileSizeMb { get; set; } = 100;

    // Громкость аудио (0-100)
    public int AudioVolume { get; set; } = 50;

    public string Author { get; set; } = "zes";
    public string GitHubUrl { get; set; } = "https://github.com/moonsoir/hoverview";
    
    private string _version = "1.0.0";
    public string Version 
    { 
        get => _version;
        set
        {
            if (Regex.IsMatch(value, @"^\d+\.\d+\.\d+$"))
                _version = value;
            else
                _version = "1.0.0";
        }
    }
}

public static class SettingsManager
{
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
        "HoverView");
    
    private static readonly string SettingsPath = Path.Combine(AppDataDir, "settings.json");

    public static AppSettings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            Current = new AppSettings();
        }
    }

    public static void Save()
    {
        try
        {
            if (!Directory.Exists(AppDataDir))
                Directory.CreateDirectory(AppDataDir);

            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Ошибка сохранения настроек: {ex.Message}", "Ошибка", 
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }
}