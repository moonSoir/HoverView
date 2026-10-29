using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HoverView;

public enum FullScreenCheckMode
{
    OnlyExclusive = 0, // Только эксклюзивный полный экран (QUNS_RUNNING_D3D_FULL_SCREEN)
    AllFullScreen = 1  // Все программы, считающиеся полноэкранными (включая QUNS_BUSY)
}

public class AppSettings
{
    public double WindowWidth { get; set; } = 600;
    public double WindowHeight { get; set; } = 600;
    public bool AutoStart { get; set; } = false;

    public bool AutoSizeToContent { get; set; } = false;
    public bool UseMinimalFrame { get; set; } = false;

    public string Language { get; set; } = "en"; // "ru" или "en"

    // Кэш
    public int MaxCacheSize { get; set; } = 50;
    public int MaxCacheSizeMb { get; set; } = 200;

    // Задержки
    public int HoverDelayMs { get; set; } = 300;
    public int HideDelayMs { get; set; } = 500;

    // Полноэкранные приложения
    public bool PauseOnFullScreenApp { get; set; } = true;
    public FullScreenCheckMode FullScreenMode { get; set; } = FullScreenCheckMode.AllFullScreen;

    // Лимиты
    public int MaxArchiveEntries { get; set; } = 500; // 0 = без ограничений
    public int MaxTextLines { get; set; } = 500;      // 0 = без ограничений

    // Архивы
    public bool ExpandArchiveTree { get; set; } = true;

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
    public List<string> ArchiveFormats { get; set; } = new() 
    { 
        ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz" 
    };

    public int MaxFileSizeMb { get; set; } = 100;
    public int AudioVolume { get; set; } = 50;

    public string Author { get; set; } = "zes";
    public string GitHubUrl { get; set; } = "https://github.com/moonsoir/HoverView";
    
    private string _version = "1.1.0";
    public string Version 
    { 
        get => _version;
        set
        {
            if (Regex.IsMatch(value, @"^\d+\.\d+\.\d+$"))
                _version = value;
            else
                _version = "1.1.0";
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
            Localization.SetLanguage(Current.Language);
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
