using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HoverView.UI;

public partial class HoverPreviewWindow : Window
{
    private bool _isAudioPlaying = false;
    private string _currentAudioPath = "";
    private string _currentVideoPath = "";
    
    private double _baseWindowWidth = 600;
    private double _baseWindowHeight = 600;
    private const double FramePadding = 10.0;

    // --- Кэш изображений ---
    private static readonly Dictionary<string, BitmapImage> _imageCache = new();
    private static readonly LinkedList<string> _cacheLru = new();
    private static int MaxCacheSize => SettingsManager.Current.MaxCacheSize;

    // --- Текущий отображаемый файл ---
    public string? CurrentFilePath { get; private set; }

    public HoverPreviewWindow()
    {
        InitializeComponent();
        PreviewVideo.MediaEnded += OnAudioMediaEnded;
        PreviewVideo.MediaFailed += OnMediaFailed;
        PreviewVideo.MediaOpened += OnMediaOpenedInfo;
    }

    public void SetBaseSize(double width, double height)
    {
        _baseWindowWidth = width;
        _baseWindowHeight = height;
        
        if (Visibility != Visibility.Visible)
        {
            Width = width;
            Height = height;
        }
    }

    public void ShowPreviewAtCursor(string filePath, int cursorX, int cursorY)
    {
        CurrentFilePath = filePath;

        ClearPreviewContent();
        Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

        Width = _baseWindowWidth;
        Height = _baseWindowHeight;

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var settings = SettingsManager.Current;
        bool success = false;

        if (settings.ImageFormats.Contains(ext))
            success = TryShowImage(filePath);
        else if (settings.VideoFormats.Contains(ext))
            success = TryShowVideo(filePath);
        else if (settings.AudioFormats.Contains(ext))
            success = TryShowAudio(filePath);
        else if (settings.TextFormats.Contains(ext))
            success = TryShowText(filePath);

        if (!success)
        {
            HidePreview();
            return;
        }

        CalculatePosition(cursorX, cursorY);

        if (Visibility != Visibility.Visible)
            Visibility = Visibility.Visible;
        Topmost = true;
    }

    private void ClearPreviewContent()
    {
        PreviewVideo.Stop();
        PreviewVideo.Source = null;
        PreviewVideo.Visibility = Visibility.Collapsed;
        
        PreviewImage.Source = null;
        PreviewImage.Tag = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        
        AudioPanel.Visibility = Visibility.Collapsed;
        AudioFileName.Text = string.Empty;
        AudioStatus.Text = string.Empty;
        
        TextScroll.Visibility = Visibility.Collapsed;
        PreviewText.Text = string.Empty;
        
        FileInfoText.Text = string.Empty;
        
        _isAudioPlaying = false;
        _currentVideoPath = "";
        _currentAudioPath = "";
    }

    // --- Изображения (с кэшем) ---
    private bool TryShowImage(string path)
    {
        try
        {
            BitmapImage bmp;

            if (_imageCache.TryGetValue(path, out var cachedBmp))
            {
                bmp = cachedBmp;
                _cacheLru.Remove(path);
                _cacheLru.AddLast(path);
            }
            else
            {
                bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();

                AddToCache(path, bmp);
            }

            PreviewImage.Tag = bmp;
            PreviewImage.Visibility = Visibility.Visible;

            if (SettingsManager.Current.AutoSizeToContent)
                AdjustWindowToContent(bmp.PixelWidth, bmp.PixelHeight);
            else if (SettingsManager.Current.UseMinimalFrame)
                AdjustWindowToContentWithMinimalFrame(bmp.PixelWidth, bmp.PixelHeight);

            var fi = new FileInfo(path);
            FileInfoText.Text = $"Изображение: {bmp.PixelWidth}x{bmp.PixelHeight} | Вес: {FormatFileSize(fi.Length)}";

            return true;
        }
        catch { return false; }
    }

    private static void AddToCache(string path, BitmapImage bmp)
    {
        if (_imageCache.ContainsKey(path))
        {
            _imageCache[path] = bmp;
            _cacheLru.Remove(path);
            _cacheLru.AddLast(path);
            return;
        }

        if (_imageCache.Count >= MaxCacheSize)
        {
            var oldest = _cacheLru.First?.Value;
            if (oldest != null)
            {
                _imageCache.Remove(oldest);
                _cacheLru.RemoveFirst();
            }
        }

        _imageCache[path] = bmp;
        _cacheLru.AddLast(path);
    }

    // --- Видео ---
    private bool TryShowVideo(string path)
    {
        try
        {
            _currentVideoPath = path;
            PreviewVideo.Volume = 0;
            PreviewVideo.Source = new Uri(path, UriKind.Absolute);
            PreviewVideo.Visibility = Visibility.Visible;
            
            var fi = new FileInfo(path);
            FileInfoText.Text = $"Видео: загрузка... | Вес: {FormatFileSize(fi.Length)}";
            
            PreviewVideo.Play();

            if (SettingsManager.Current.AutoSizeToContent || SettingsManager.Current.UseMinimalFrame)
                PreviewVideo.MediaOpened += OnVideoMediaOpenedSize;

            return true;
        }
        catch { return false; }
    }

    private void OnVideoMediaOpenedSize(object? sender, RoutedEventArgs e)
    {
        PreviewVideo.MediaOpened -= OnVideoMediaOpenedSize;

        if (PreviewVideo.NaturalVideoWidth > 0 && PreviewVideo.NaturalVideoHeight > 0)
        {
            if (SettingsManager.Current.AutoSizeToContent)
                AdjustWindowToContent(PreviewVideo.NaturalVideoWidth, PreviewVideo.NaturalVideoHeight);
            else if (SettingsManager.Current.UseMinimalFrame)
                AdjustWindowToContentWithMinimalFrame(PreviewVideo.NaturalVideoWidth, PreviewVideo.NaturalVideoHeight);
            
            var pos = System.Windows.Forms.Control.MousePosition;
            CalculatePosition(pos.X, pos.Y);
        }
    }

    // --- Аудио ---
    private bool TryShowAudio(string path)
    {
        try
        {
            _currentAudioPath = path;
            PreviewVideo.Volume = SettingsManager.Current.AudioVolume / 100.0;
            PreviewVideo.Source = new Uri(path, UriKind.Absolute);
            PreviewVideo.Visibility = Visibility.Collapsed;
            
            AudioFileName.Text = Path.GetFileName(path);
            AudioPanel.Visibility = Visibility.Visible;
            
            var fi = new FileInfo(path);
            FileInfoText.Text = $"Аудио: загрузка... | Вес: {FormatFileSize(fi.Length)}";
            
            PreviewVideo.Play();
            _isAudioPlaying = true;
            UpdateAudioButton();
            return true;
        }
        catch { return false; }
    }

    // --- Текст ---
    private bool TryShowText(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            
            long maxTextSize = 1 * 1024 * 1024;
            if (fi.Length > maxTextSize)
            {
                PreviewText.Text = $"Файл слишком большой для предпросмотра ({fi.Length / 1024} КБ).\nМаксимум: 1 МБ.";
                TextScroll.Visibility = Visibility.Visible;
                FileInfoText.Text = $"Текст: {FormatFileSize(fi.Length)}";
                return true;
            }

            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var sb = new StringBuilder();
            int lineCount = 0;
            const int maxLines = 500;

            while (!reader.EndOfStream && lineCount < maxLines)
            {
                sb.AppendLine(reader.ReadLine());
                lineCount++;
            }

            if (lineCount >= maxLines)
                sb.AppendLine("\n... (файл обрезан для предпросмотра)");

            PreviewText.Text = sb.ToString();
            TextScroll.Visibility = Visibility.Visible;
            FileInfoText.Text = $"Текст: {FormatFileSize(fi.Length)}";
            return true;
        }
        catch { return false; }
    }

    // --- Обработка открытия медиа для обновления информации ---
    private void OnMediaOpenedInfo(object? sender, RoutedEventArgs e)
    {
        if (PreviewVideo.Visibility == Visibility.Visible && !string.IsNullOrEmpty(_currentVideoPath))
        {
            try
            {
                var fi = new FileInfo(_currentVideoPath);
                string duration = FormatDuration(PreviewVideo.NaturalDuration.TimeSpan);
                FileInfoText.Text = $"Видео: {PreviewVideo.NaturalVideoWidth}x{PreviewVideo.NaturalVideoHeight} | Вес: {FormatFileSize(fi.Length)} | Длительность: {duration}";
            }
            catch { }
        }
        else if (AudioPanel.Visibility == Visibility.Visible && !string.IsNullOrEmpty(_currentAudioPath))
        {
            try
            {
                var fi = new FileInfo(_currentAudioPath);
                string duration = FormatDuration(PreviewVideo.NaturalDuration.TimeSpan);
                double bitrate = (fi.Length * 8.0) / PreviewVideo.NaturalDuration.TimeSpan.TotalSeconds / 1000.0;
                FileInfoText.Text = $"Аудио: {bitrate:0} кбит/с | Вес: {FormatFileSize(fi.Length)} | Длительность: {duration}";
            }
            catch { }
        }
    }

    // --- Вспомогательные методы ---

    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
        int counter = 0;
        double number = bytes;
        while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:0.##} {suffixes[counter]}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private void AdjustWindowToContent(double contentWidth, double contentHeight)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        double contentDpiWidth = contentWidth / dpi.DpiScaleX;
        double contentDpiHeight = contentHeight / dpi.DpiScaleY;

        if (contentDpiWidth < _baseWindowWidth) Width = contentDpiWidth;
        if (contentDpiHeight < _baseWindowHeight) Height = contentDpiHeight;
    }

    private void AdjustWindowToContentWithMinimalFrame(double contentWidth, double contentHeight)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        
        double contentDpiWidth = contentWidth / dpi.DpiScaleX;
        double contentDpiHeight = contentHeight / dpi.DpiScaleY;

        double targetWidth = contentDpiWidth;
        double targetHeight = contentDpiHeight;

        if (contentDpiWidth > _baseWindowWidth || contentDpiHeight > _baseWindowHeight)
        {
            double scaleX = _baseWindowWidth / contentDpiWidth;
            double scaleY = _baseWindowHeight / contentDpiHeight;
            double scale = Math.Min(scaleX, scaleY);

            targetWidth = contentDpiWidth * scale;
            targetHeight = contentDpiHeight * scale;
        }

        Width = targetWidth + (FramePadding * 2);
        Height = targetHeight + (FramePadding * 2);
    }

    // --- Обработчики событий плеера ---
    private void OnAudioMediaEnded(object? sender, RoutedEventArgs e)
    {
        _isAudioPlaying = false;
        UpdateAudioButton();
    }

    private void OnMediaFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        AudioPanel.Visibility = Visibility.Collapsed;
        _isAudioPlaying = false;
    }

    private void BtnAudioPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (PreviewVideo.Source == null) return;

        if (_isAudioPlaying)
        {
            PreviewVideo.Pause();
            _isAudioPlaying = false;
        }
        else
        {
            PreviewVideo.Play();
            _isAudioPlaying = true;
        }
        UpdateAudioButton();
    }

    private void UpdateAudioButton()
    {
        BtnAudioPlayPause.Content = _isAudioPlaying ? "⏸" : "▶";
        AudioStatus.Text = _isAudioPlaying ? "Воспроизведение" : "Пауза";
    }

    // --- Позиционирование ---
    private void CalculatePosition(int physicalX, int physicalY)
    {
        var monitorDpi = GetDpiForPoint(physicalX, physicalY);
        double cursorDpiX = physicalX / monitorDpi.ScaleX;
        double cursorDpiY = physicalY / monitorDpi.ScaleY;

        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(physicalX, physicalY));
        var workArea = screen.WorkingArea;

        double areaLeft = workArea.Left / monitorDpi.ScaleX;
        double areaTop = workArea.Top / monitorDpi.ScaleY;
        double areaRight = workArea.Right / monitorDpi.ScaleX;
        double areaBottom = workArea.Bottom / monitorDpi.ScaleY;

        const double offset = 20;
        double targetLeft = cursorDpiX + offset;
        double targetTop = cursorDpiY + offset;

        if (targetLeft + Width > areaRight) targetLeft = cursorDpiX - Width - offset;
        if (targetTop + Height > areaBottom) targetTop = cursorDpiY - Height - offset;
        if (targetLeft < areaLeft) targetLeft = areaLeft;
        if (targetTop < areaTop) targetTop = areaTop;

        Left = targetLeft;
        Top = targetTop;
    }

    public void HidePreview()
    {
        CurrentFilePath = null;
        ClearPreviewContent();
        Visibility = Visibility.Collapsed;
    }

    // --- DPI и Win32 ---
    private struct MonitorDpi { public double ScaleX; public double ScaleY; }

    private MonitorDpi GetDpiForPoint(int x, int y)
    {
        try
        {
            var point = new POINT { X = x, Y = y };
            var monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
            if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == 0)
                return new MonitorDpi { ScaleX = dpiX / 96.0, ScaleY = dpiY / 96.0 };
        }
        catch { }
        var dpi = VisualTreeHelper.GetDpi(this);
        return new MonitorDpi { ScaleX = dpi.DpiScaleX, ScaleY = dpi.DpiScaleY };
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }
}