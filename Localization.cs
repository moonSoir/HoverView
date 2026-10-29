using System.Globalization;
using System.Resources;
using System.Threading;

namespace HoverView;

public static class Localization
{
    private static ResourceManager? _resourceManager;

    public static void SetLanguage(string cultureName)
    {
        var culture = new CultureInfo(cultureName);
        Thread.CurrentThread.CurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;

        _resourceManager = new ResourceManager("HoverView.Resources.Strings", typeof(Localization).Assembly);
    }

    public static string Get(string key)
    {
        if (_resourceManager == null)
        {
            _resourceManager = new ResourceManager("HoverView.Resources.Strings", typeof(Localization).Assembly);
        }
        return _resourceManager.GetString(key) ?? key;
    }

    // Статические свойства для XAML
    public static string SettingsTitle => Get("SettingsTitle");
    public static string TabAppearance => Get("TabAppearance");
    public static string TabFormats => Get("TabFormats");
    public static string TabSound => Get("TabSound");
    public static string TabSystem => Get("TabSystem");
    public static string TabAbout => Get("TabAbout");
    public static string Close => Get("Close");
    public static string Save => Get("Save");
    public static string Cancel => Get("Cancel");
    public static string Version => Get("Version");
    public static string Author => Get("Author");
    public static string Language => Get("Language");
    
    // Дополнительные ключи для полей (можно добавить по мере необходимости)
    public static string WindowSizeLimit => Get("WindowSizeLimit");
    public static string Width => Get("Width");
    public static string Height => Get("Height");
    public static string DisplayMode => Get("DisplayMode");
    public static string AutoSizeToContent => Get("AutoSizeToContent");
    public static string UseMinimalFrame => Get("UseMinimalFrame");
    public static string Delays => Get("Delays");
    public static string HoverDelay => Get("HoverDelay");
    public static string HideDelay => Get("HideDelay");
    public static string ArchiveTree => Get("ArchiveTree");
    public static string ExpandArchiveTree => Get("ExpandArchiveTree");
    public static string Formats => Get("Formats");
    public static string Images => Get("Images");
    public static string Videos => Get("Videos");
    public static string Audio => Get("Audio");
    public static string Text => Get("Text");
    public static string Archives => Get("Archives");
    public static string MaxFileSize => Get("MaxFileSize");
    public static string MaxArchiveEntries => Get("MaxArchiveEntries");
    public static string MaxTextLines => Get("MaxTextLines");
    public static string CacheSizeCount => Get("CacheSizeCount");
    public static string CacheSizeMb => Get("CacheSizeMb");
    public static string AudioSettings => Get("AudioSettings");
    public static string Volume => Get("Volume");
    public static string VideoNoSound => Get("VideoNoSound");
    public static string Startup => Get("Startup");
    public static string AutoStart => Get("AutoStart");
    public static string FullScreenApps => Get("FullScreenApps");
    public static string PauseOnFullScreen => Get("PauseOnFullScreen");
    public static string Mode => Get("Mode");
    public static string OnlyExclusive => Get("OnlyExclusive");
    public static string AllFullScreen => Get("AllFullScreen");
    public static string About => Get("About");
    public static string Info => Get("Info");
    public static string InfoImage => Get("InfoImage");
    public static string InfoVideo => Get("InfoVideo");
    public static string InfoAudio => Get("InfoAudio");
    public static string InfoText => Get("InfoText");
    public static string InfoArchive => Get("InfoArchive");
    public static string InfoWeight => Get("InfoWeight");
    public static string InfoDuration => Get("InfoDuration");
    public static string InfoBitrate => Get("InfoBitrate");
    public static string InfoLoading => Get("InfoLoading");
    public static string InfoFiles => Get("InfoFiles");
    public static string SizeByte => Get("SizeByte");
    public static string SizeKb => Get("SizeKb");
    public static string SizeMb => Get("SizeMb");
    public static string SizeGb => Get("SizeGb");
    public static string SizeTb => Get("SizeTb");
    public static string AudioPlaying => Get("AudioPlaying");
    public static string AudioPaused => Get("AudioPaused");
    public static string ErrorTitle => Get("ErrorTitle");
    public static string ErrorWidth => Get("ErrorWidth");
    public static string ErrorHeight => Get("ErrorHeight");
    public static string ErrorHoverDelay => Get("ErrorHoverDelay");
    public static string ErrorHideDelay => Get("ErrorHideDelay");
    public static string ErrorMaxSize => Get("ErrorMaxSize");
    public static string ErrorMaxArchiveEntries => Get("ErrorMaxArchiveEntries");
    public static string ErrorMaxTextLines => Get("ErrorMaxTextLines");
    public static string ErrorMaxCacheSize => Get("ErrorMaxCacheSize");
    public static string ErrorMaxCacheSizeMb => Get("ErrorMaxCacheSizeMb");
    public static string ErrorAutoStartTitle => Get("ErrorAutoStartTitle");
    public static string ErrorAutoStartMessage => Get("ErrorAutoStartMessage");
}