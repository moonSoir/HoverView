using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SharpCompress.Readers;

namespace HoverView.UI;

public partial class PreviewWindow : Window
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
    private static long _currentCacheBytes = 0;
    private static int MaxCacheSize => SettingsManager.Current.MaxCacheSize;
    private static long MaxCacheBytes => SettingsManager.Current.MaxCacheSizeMb * 1024L * 1024L;

    // Токен отмены для асинхронной загрузки
    private CancellationTokenSource? _loadCts;

    public string? CurrentFilePath { get; private set; }

    // --- Win32 для ForceTopmost ---
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    public PreviewWindow()
    {
        InitializeComponent();
        PreviewVideo.MediaEnded += OnAudioMediaEnded;
        PreviewVideo.MediaFailed += OnMediaFailed;
        PreviewVideo.MediaOpened += OnMediaOpenedInfo;
    }

    public void ForceTopmost()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
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

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();

        ClearPreviewContent();
        Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

        Width = _baseWindowWidth;
        Height = _baseWindowHeight;

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var settings = SettingsManager.Current;
        bool success = false;

        if (settings.ImageFormats.Contains(ext))
        {
            _ = LoadImageAsync(filePath, _loadCts.Token);
            success = true;
        }
        else if (settings.VideoFormats.Contains(ext))
            success = TryShowVideo(filePath);
        else if (settings.AudioFormats.Contains(ext))
            success = TryShowAudio(filePath);
        else if (settings.TextFormats.Contains(ext))
            success = TryShowText(filePath);
        else if (settings.ArchiveFormats.Contains(ext))
            success = TryShowArchive(filePath);

        if (!success)
        {
            HidePreview();
            return;
        }

        CalculatePosition(cursorX, cursorY);

        if (Visibility != Visibility.Visible)
            Visibility = Visibility.Visible;
        Topmost = true;

        ForceTopmost();
    }

    private void ClearPreviewContent()
    {
        PreviewVideo.Stop();
        PreviewVideo.Close();
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
        
        ArchiveTree.Items.Clear();
        ArchiveTree.Visibility = Visibility.Collapsed;
        
        FileInfoText.Text = string.Empty;
        
        _isAudioPlaying = false;
        _currentVideoPath = "";
        _currentAudioPath = "";
    }

    // --- Асинхронная загрузка изображений ---
    private async Task LoadImageAsync(string path, CancellationToken token)
    {
        try
        {
            BitmapImage? bmp = null;
            int origWidth = 0, origHeight = 0;

            if (_imageCache.TryGetValue(path, out var cachedBmp))
            {
                bmp = cachedBmp;
                _cacheLru.Remove(path);
                _cacheLru.AddLast(path);
                GetOriginalDimensions(path, out origWidth, out origHeight);
            }
            else
            {
                var dpi = GetCurrentDpi();

                var result = await Task.Run<(BitmapImage? Image, int OrigWidth, int OrigHeight)>(() =>
                {
                    if (token.IsCancellationRequested) return (null, 0, 0);

                    int decodeWidth = 0;
                    int decodeHeight = 0;

                    if (SettingsManager.Current.AutoSizeToContent || SettingsManager.Current.UseMinimalFrame)
                    {
                        double maxDpiW = _baseWindowWidth * dpi.ScaleX;
                        double maxDpiH = _baseWindowHeight * dpi.ScaleY;
                        decodeWidth = (int)Math.Min(maxDpiW, 4000);
                        decodeHeight = (int)Math.Min(maxDpiH, 4000);
                    }
                    else
                    {
                        double targetW = _baseWindowWidth;
                        double targetH = _baseWindowHeight;
                        decodeWidth = (int)(targetW * dpi.ScaleX);
                        decodeHeight = (int)(targetH * dpi.ScaleY);
                    }

                    if (decodeWidth <= 0) decodeWidth = 1000;
                    if (decodeHeight <= 0) decodeHeight = 1000;

                    GetOriginalDimensions(path, out int ow, out int oh);

                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri(path, UriKind.Absolute);
                    image.DecodePixelWidth = decodeWidth;
                    image.EndInit();
                    image.Freeze();

                    return (image, ow, oh);
                }, token);

                if (token.IsCancellationRequested || result.Image == null) return;

                bmp = result.Image;
                origWidth = result.OrigWidth;
                origHeight = result.OrigHeight;

                AddToCache(path, bmp);
            }

            if (token.IsCancellationRequested) return;

            await Dispatcher.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;

                PreviewImage.Tag = bmp;
                PreviewImage.Visibility = Visibility.Visible;

                if (SettingsManager.Current.AutoSizeToContent)
                    AdjustWindowToContent(bmp.PixelWidth, bmp.PixelHeight);
                else if (SettingsManager.Current.UseMinimalFrame)
                    AdjustWindowToContentWithMinimalFrame(bmp.PixelWidth, bmp.PixelHeight);

                var fi = new FileInfo(path);
                string dims = origWidth > 0 && origHeight > 0 ? $"{origWidth}x{origHeight}" : $"{bmp.PixelWidth}x{bmp.PixelHeight}";
                FileInfoText.Text = $"{Localization.Get("InfoImage")} {dims} | {Localization.Get("InfoWeight")} {FormatFileSize(fi.Length)}";

                var pos = System.Windows.Forms.Control.MousePosition;
                CalculatePosition(pos.X, pos.Y);
            });
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private struct DpiScale { public double ScaleX; public double ScaleY; }

    private DpiScale GetCurrentDpi()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return new DpiScale { ScaleX = dpi.DpiScaleX, ScaleY = dpi.DpiScaleY };
    }

    private static void GetOriginalDimensions(string path, out int width, out int height)
    {
        width = 0;
        height = 0;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder.Frames.Count > 0)
            {
                width = decoder.Frames[0].PixelWidth;
                height = decoder.Frames[0].PixelHeight;
            }
        }
        catch { }
    }

    // --- Кэш с двумя ограничениями ---
    private static void AddToCache(string path, BitmapImage bmp)
    {
        if (_imageCache.ContainsKey(path))
        {
            _imageCache[path] = bmp;
            _cacheLru.Remove(path);
            _cacheLru.AddLast(path);
            return;
        }

        long imageSize = (long)bmp.PixelWidth * bmp.PixelHeight * 4;

        while (_imageCache.Count >= MaxCacheSize || 
               (_currentCacheBytes + imageSize > MaxCacheBytes && _imageCache.Count > 0))
        {
            var oldest = _cacheLru.First?.Value;
            if (oldest == null) break;

            if (_imageCache.TryGetValue(oldest, out var oldBmp))
            {
                long oldSize = (long)oldBmp.PixelWidth * oldBmp.PixelHeight * 4;
                _currentCacheBytes -= oldSize;
            }

            _imageCache.Remove(oldest);
            _cacheLru.RemoveFirst();
        }

        _imageCache[path] = bmp;
        _cacheLru.AddLast(path);
        _currentCacheBytes += imageSize;
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
            FileInfoText.Text = $"{Localization.Get("InfoVideo")} {Localization.Get("InfoLoading")} | {Localization.Get("InfoWeight")} {FormatFileSize(fi.Length)}";
            
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
            FileInfoText.Text = $"{Localization.Get("InfoAudio")} {Localization.Get("InfoLoading")} | {Localization.Get("InfoWeight")} {FormatFileSize(fi.Length)}";
            
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
                PreviewText.Text = string.Format(Localization.Get("InfoTextTooLarge"), fi.Length / 1024, 1);
                TextScroll.Visibility = Visibility.Visible;
                FileInfoText.Text = $"{Localization.Get("InfoText")} {FormatFileSize(fi.Length)}";
                return true;
            }

            int maxLines = SettingsManager.Current.MaxTextLines;
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var sb = new StringBuilder();
            int lineCount = 0;

            while (!reader.EndOfStream && (maxLines == 0 || lineCount < maxLines))
            {
                sb.AppendLine(reader.ReadLine());
                lineCount++;
            }

            if (maxLines > 0 && lineCount >= maxLines)
                sb.AppendLine("\n" + Localization.Get("InfoTextTruncated"));

            PreviewText.Text = sb.ToString();
            TextScroll.Visibility = Visibility.Visible;
            FileInfoText.Text = $"{Localization.Get("InfoText")} {FormatFileSize(fi.Length)}";
            return true;
        }
        catch { return false; }
    }

    // --- Архив (дерево) ---
    private bool TryShowArchive(string path)
    {
        try
        {
            var fi = new FileInfo(path);

            ArchiveTree.Items.Clear();
            ArchiveTree.Visibility = Visibility.Visible;
            TextScroll.Visibility = Visibility.Collapsed;
            PreviewText.Text = string.Empty;

            int maxEntries = SettingsManager.Current.MaxArchiveEntries;
            int count = 0;

            var rootNode = new TreeViewItem
            {
                Header = $"{Path.GetFileName(path)} ({FormatFileSize(fi.Length)})",
                IsExpanded = SettingsManager.Current.ExpandArchiveTree
            };
            ArchiveTree.Items.Add(rootNode);

            var nodeMap = new Dictionary<string, TreeViewItem>(StringComparer.OrdinalIgnoreCase)
            {
                { "", rootNode }
            };

            using (var stream = File.OpenRead(path))
            using (var reader = ReaderFactory.OpenReader(stream))
            {
                while (reader.MoveToNextEntry())
                {
                    if (maxEntries > 0 && count >= maxEntries)
                    {
                        rootNode.Items.Add(new TreeViewItem { Header = string.Format(Localization.Get("InfoArchiveTruncated"), maxEntries) });
                        break;
                    }

                    var entry = reader.Entry;
                    var fullKey = entry.Key.Replace('\\', '/').Trim('/');
                    if (string.IsNullOrEmpty(fullKey)) continue;

                    var parts = fullKey.Split('/');
                    string currentPath = "";
                    TreeViewItem parentNode = rootNode;

                    for (int i = 0; i < parts.Length; i++)
                    {
                        var part = parts[i];
                        if (string.IsNullOrEmpty(part)) continue;

                        var isLast = (i == parts.Length - 1);
                        var nodeKey = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}/{part}";

                        if (!nodeMap.TryGetValue(nodeKey, out var node))
                        {
                            node = new TreeViewItem();
                            if (isLast && !entry.IsDirectory)
                                node.Header = $"{part} ({FormatFileSize(entry.Size)})";
                            else
                            {
                                node.Header = $"[{part}]";
                                node.IsExpanded = SettingsManager.Current.ExpandArchiveTree;
                            }
                            parentNode.Items.Add(node);
                            nodeMap[nodeKey] = node;
                        }

                        parentNode = node;
                        currentPath = nodeKey;
                    }

                    count++;
                }
            }

            FileInfoText.Text = $"{Localization.Get("InfoArchive")} {FormatFileSize(fi.Length)} | {Localization.Get("InfoFiles")} {count}";
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
                FileInfoText.Text = $"{Localization.Get("InfoVideo")} {PreviewVideo.NaturalVideoWidth}x{PreviewVideo.NaturalVideoHeight} | {Localization.Get("InfoWeight")} {FormatFileSize(fi.Length)} | {Localization.Get("InfoDuration")} {duration}";
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
                FileInfoText.Text = $"{Localization.Get("InfoAudio")} {bitrate:0} {Localization.Get("InfoBitrate")} | {Localization.Get("InfoWeight")} {FormatFileSize(fi.Length)} | {Localization.Get("InfoDuration")} {duration}";
            }
            catch { }
        }
    }

    // --- Вспомогательные методы ---
    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = 
        { 
            Localization.Get("SizeByte"), 
            Localization.Get("SizeKb"), 
            Localization.Get("SizeMb"), 
            Localization.Get("SizeGb"), 
            Localization.Get("SizeTb") 
        };
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
        AudioStatus.Text = _isAudioPlaying ? Localization.Get("AudioPlaying") : Localization.Get("AudioPaused");
    }
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
