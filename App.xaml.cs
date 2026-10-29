using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Forms;
using HoverView.Core.FileResolver;
using HoverView.UI;

#nullable enable

namespace HoverView;

public partial class App : System.Windows.Application
{
    private DispatcherTimer? _timer;
    private ExplorerFileResolver _resolver = new();
    private PreviewWindow? _previewWindow;
    private NotifyIcon? _notifyIcon;

    private string? _lastFilePath;
    private string? _currentShownFilePath;
    private DateTime _hoverStartTime = DateTime.MinValue;
    private DateTime _leaveStartTime = DateTime.MinValue;
    private string? _pendingFilePath;

    // Адаптивный таймер
    private System.Drawing.Point _lastMousePos;
    private DateTime _lastMouseMoveTime = DateTime.MinValue;
    private const int FastInterval = 50;
    private const int MediumInterval = 150;
    private const int SlowInterval = 500;
    private const int VerySlowInterval = 1000;

    // Кэш поддерживаемых расширений
    private HashSet<string> _supportedExtensions = new(StringComparer.OrdinalIgnoreCase);
    private string? _lastCheckedFolder;
    private DateTime _lastFolderCheckTime = DateTime.MinValue;
    private bool? _lastFolderHasSupported;

    // Флаг для отслеживания нахождения курсора над окном предпросмотра (для текста и архивов)
    private bool _wasCursorInsidePreview = false;

    // Для принудительного поднятия окна (не чаще раза в 100 мс)
    private DateTime _lastForceTopmostTime = DateTime.MinValue;

    // Win32
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SHQueryUserNotificationState(out QUERY_USER_NOTIFICATION_STATE state);

    private enum QUERY_USER_NOTIFICATION_STATE
    {
        QUNS_NOT_PRESENT = 1,
        QUNS_BUSY = 2,
        QUNS_RUNNING_D3D_FULL_SCREEN = 3,
        QUNS_PRESENTATION_MODE = 4,
        QUNS_ACCEPTS_NOTIFICATIONS = 5,
        QUNS_QUIET_TIME = 6,
        QUNS_APP = 7
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint GA_ROOT = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        SettingsManager.Load();
        Localization.SetLanguage(SettingsManager.Current.Language);
        UpdateSupportedExtensions();

        InitializeTrayIcon();

        _previewWindow = new PreviewWindow();
        ApplyWindowSettings();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FastInterval) };
        _timer.Tick += OnTick;
        _timer.Start();

        _lastMousePos = Control.MousePosition;
    }

    private void UpdateSupportedExtensions()
    {
        _supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ext in SettingsManager.Current.ImageFormats) _supportedExtensions.Add(ext);
        foreach (var ext in SettingsManager.Current.VideoFormats) _supportedExtensions.Add(ext);
        foreach (var ext in SettingsManager.Current.AudioFormats) _supportedExtensions.Add(ext);
        foreach (var ext in SettingsManager.Current.TextFormats) _supportedExtensions.Add(ext);
        foreach (var ext in SettingsManager.Current.ArchiveFormats) _supportedExtensions.Add(ext);
    }

    private void ApplyWindowSettings()
    {
        if (_previewWindow == null) return;
        _previewWindow.SetBaseSize(
            SettingsManager.Current.WindowWidth,
            SettingsManager.Current.WindowHeight);
    }

    private void InitializeTrayIcon()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = Localization.Get("TrayTooltip")
        };

        RefreshTrayMenu();

        _notifyIcon.DoubleClick += OnSettingsClicked;
    }

    /// <summary>
    /// Пересоздаёт контекстное меню трея с учётом текущего языка.
    /// Вызывается при старте и после смены языка в настройках.
    /// </summary>
    public void RefreshTrayMenu()
    {
        if (_notifyIcon == null) return;

        _notifyIcon.Text = Localization.Get("TrayTooltip");

        var contextMenu = new ContextMenuStrip();

        var settingsItem = new ToolStripMenuItem(Localization.Get("TraySettings"));
        settingsItem.Click += OnSettingsClicked;
        contextMenu.Items.Add(settingsItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem(Localization.Get("TrayExit"));
        exitItem.Click += OnExitClicked;
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    private void OnSettingsClicked(object? sender, EventArgs e)
    {
        _previewWindow?.HidePreview();
        _currentShownFilePath = null;
        _leaveStartTime = DateTime.MinValue;
        _pendingFilePath = null;
        _lastFilePath = null;
        _wasCursorInsidePreview = false;

        var settingsWindow = new SettingsWindow();
        if (settingsWindow.ShowDialog() == true)
        {
            UpdateSupportedExtensions();
            ApplyWindowSettings();
            RefreshTrayMenu();
        }
    }

    private void OnExitClicked(object? sender, EventArgs e)
    {
        _timer?.Stop();
        _previewWindow?.HidePreview();
        _previewWindow?.Close();
        _notifyIcon?.Dispose();

        System.Windows.Application.Current.Shutdown();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var pos = Control.MousePosition;
        var now = DateTime.UtcNow;

        bool mouseMoved = pos != _lastMousePos;
        if (mouseMoved)
        {
            _lastMousePos = pos;
            _lastMouseMoveTime = now;
        }

        var (interval, isExplorerForeground) = DetermineAdaptiveInterval(pos, mouseMoved, now);
        _timer!.Interval = TimeSpan.FromMilliseconds(interval);

        if (isExplorerForeground)
        {
            var filePath = _resolver.ResolveFileAtPoint(pos.X, pos.Y);
            if (filePath != _lastFilePath)
            {
                if (_lastFilePath != null)
                {
                    if (_currentShownFilePath == _lastFilePath)
                        _leaveStartTime = now;
                    if (_pendingFilePath == _lastFilePath)
                        _pendingFilePath = null;
                }

                _lastFilePath = filePath;

                if (filePath != null)
                {
                    if (filePath == _currentShownFilePath)
                        _leaveStartTime = DateTime.MinValue;
                    else
                    {
                        _hoverStartTime = now;
                        _pendingFilePath = filePath;
                    }
                }
                else
                {
                    _pendingFilePath = null;
                }
            }
        }
        else
        {
            if (_lastFilePath != null)
            {
                if (_currentShownFilePath == _lastFilePath)
                    _leaveStartTime = now;
                _lastFilePath = null;
                _pendingFilePath = null;
            }
        }

        // Удержание окна для текста и архивов
        if (_previewWindow != null && _previewWindow.IsVisible && _currentShownFilePath != null)
        {
            var ext = Path.GetExtension(_currentShownFilePath).ToLowerInvariant();
            bool isText = SettingsManager.Current.TextFormats.Contains(ext);
            bool isArchive = SettingsManager.Current.ArchiveFormats.Contains(ext);

            if (isText || isArchive)
            {
                var previewHwnd = new System.Windows.Interop.WindowInteropHelper(_previewWindow).Handle;
                if (previewHwnd != IntPtr.Zero && GetWindowRect(previewHwnd, out RECT previewRect))
                {
                    bool cursorInsidePreview = pos.X >= previewRect.Left && pos.X <= previewRect.Right &&
                                               pos.Y >= previewRect.Top && pos.Y <= previewRect.Bottom;

                    if (cursorInsidePreview)
                    {
                        _leaveStartTime = DateTime.MinValue;
                        _wasCursorInsidePreview = true;
                    }
                    else if (_wasCursorInsidePreview)
                    {
                        _wasCursorInsidePreview = false;

                        var fileUnderCursor = _resolver.ResolveFileAtPoint(pos.X, pos.Y);
                        if (fileUnderCursor != _currentShownFilePath)
                        {
                            _leaveStartTime = now;
                        }
                    }
                }
            }
            else
            {
                _wasCursorInsidePreview = false;
            }
        }
        else
        {
            _wasCursorInsidePreview = false;
        }

        // Принудительное поднятие окна
        if (_previewWindow != null && _previewWindow.IsVisible)
        {
            if ((now - _lastForceTopmostTime).TotalMilliseconds >= 100)
            {
                _previewWindow.ForceTopmost();
                _lastForceTopmostTime = now;
            }
        }

        // Таймер исчезновения
        if (_currentShownFilePath != null && _leaveStartTime != DateTime.MinValue)
        {
            if ((now - _leaveStartTime).TotalMilliseconds >= SettingsManager.Current.HideDelayMs)
            {
                _previewWindow?.HidePreview();
                _currentShownFilePath = null;
                _leaveStartTime = DateTime.MinValue;
                _wasCursorInsidePreview = false;
            }
        }

        // Таймер появления
        if (_currentShownFilePath == null && _pendingFilePath != null)
        {
            var filePath = _resolver.ResolveFileAtPoint(pos.X, pos.Y);
            if (filePath == _pendingFilePath)
            {
                if ((now - _hoverStartTime).TotalMilliseconds >= SettingsManager.Current.HoverDelayMs)
                {
                    try
                    {
                        var fi = new FileInfo(_pendingFilePath);
                        var maxBytes = SettingsManager.Current.MaxFileSizeMb * 1024L * 1024L;
                        if (SettingsManager.Current.MaxFileSizeMb > 0 && fi.Length > maxBytes)
                        {
                            _pendingFilePath = null;
                            return;
                        }
                    }
                    catch
                    {
                        _pendingFilePath = null;
                        return;
                    }

                    _previewWindow!.ShowPreviewAtCursor(_pendingFilePath, pos.X, pos.Y);
                    _currentShownFilePath = _pendingFilePath;
                    _pendingFilePath = null;
                    _leaveStartTime = DateTime.MinValue;
                }
            }
            else
            {
                _pendingFilePath = null;
            }
        }
    }

    private (int Interval, bool IsExplorerForeground) DetermineAdaptiveInterval(System.Drawing.Point pos, bool mouseMoved, DateTime now)
    {
        var foregroundHwnd = GetForegroundWindow();
        var className = GetWindowClass(foregroundHwnd);

        if (SettingsManager.Current.PauseOnFullScreenApp &&
            className != "CabinetWClass" && className != "ExploreWClass" && IsTrueFullScreenApp())
            return (VerySlowInterval, false);

        if (!IsExplorerRunning())
            return (VerySlowInterval, false);

        var hwndUnderCursor = WindowFromPoint(new POINT { X = pos.X, Y = pos.Y });
        var rootUnderCursor = GetAncestor(hwndUnderCursor, GA_ROOT);
        var underClassName = GetWindowClass(rootUnderCursor);
        bool underExplorer = underClassName == "CabinetWClass" || underClassName == "ExploreWClass";

        if (!underExplorer)
            return (SlowInterval, false);

        bool? folderHasSupported = CheckFolderSupported(pos);
        if (folderHasSupported == false)
        {
            return (SlowInterval, true);
        }

        bool recentlyMoved = (now - _lastMouseMoveTime).TotalMilliseconds < 200;
        if (recentlyMoved || mouseMoved)
            return (FastInterval, true);

        return (MediumInterval, true);
    }

    private static bool IsTrueFullScreenApp()
    {
        try
        {
            int hr = SHQueryUserNotificationState(out var state);
            if (hr == 0)
            {
                var mode = SettingsManager.Current.FullScreenMode;
                
                if (mode == FullScreenCheckMode.OnlyExclusive)
                {
                    return state == QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN ||
                           state == QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE;
                }
                else
                {
                    return state == QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN ||
                           state == QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY ||
                           state == QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE;
                }
            }
        }
        catch { }
        return false;
    }

    private bool? CheckFolderSupported(System.Drawing.Point pos)
    {
        var now = DateTime.UtcNow;
        var filePath = _resolver.ResolveFileAtPoint(pos.X, pos.Y);
        if (string.IsNullOrEmpty(filePath)) return null;

        var folder = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(folder)) return null;

        if (_lastCheckedFolder == folder && (now - _lastFolderCheckTime).TotalSeconds < 10)
            return _lastFolderHasSupported;

        var result = _resolver.FolderHasSupportedFiles(folder, new List<string>(_supportedExtensions));
        if (result.HasValue)
        {
            _lastCheckedFolder = folder;
            _lastFolderCheckTime = now;
            _lastFolderHasSupported = result;
            return result;
        }

        return null;
    }

    private static bool IsExplorerRunning()
    {
        var hwnd = FindWindow("CabinetWClass", null);
        if (hwnd != IntPtr.Zero) return true;
        hwnd = FindWindow("ExploreWClass", null);
        return hwnd != IntPtr.Zero;
    }

    private static string GetWindowClass(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
