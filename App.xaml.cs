using System;
using System.Drawing;
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
    private HoverPreviewWindow? _previewWindow;
    private NotifyIcon? _notifyIcon;

    // Состояние
    private string? _lastFilePath;          // файл, над которым курсор в прошлом тике
    private string? _currentShownFilePath;  // файл, который сейчас показан в окне
    private DateTime _hoverStartTime = DateTime.MinValue; // когда навели на _lastFilePath
    private DateTime _leaveStartTime = DateTime.MinValue; // когда увели курсор с _currentShownFilePath
    private string? _pendingFilePath;       // файл, который ждёт показа после скрытия старого

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        SettingsManager.Load();
        InitializeTrayIcon();

        _previewWindow = new HoverPreviewWindow();
        ApplyWindowSettings();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += OnTick;
        _timer.Start();
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
            Text = "HoverView - Предпросмотр файлов"
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add(new ToolStripMenuItem("Настройки...", null, OnSettingsClicked));
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(new ToolStripMenuItem("Выход", null, OnExitClicked));

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += OnSettingsClicked;
    }

    private void OnSettingsClicked(object? sender, EventArgs e)
    {
        _previewWindow?.HidePreview();
        _currentShownFilePath = null;
        _leaveStartTime = DateTime.MinValue;
        _pendingFilePath = null;
        _lastFilePath = null;

        var settingsWindow = new SettingsWindow();
        if (settingsWindow.ShowDialog() == true)
        {
            ApplyWindowSettings();
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
        var filePath = _resolver.ResolveFileAtPoint(pos.X, pos.Y);
        var now = DateTime.UtcNow;

        // 1. Обработка смены файла под курсором
        if (filePath != _lastFilePath)
        {
            // Курсор ушёл с предыдущего файла
            if (_lastFilePath != null)
            {
                // Если это был тот файл, что сейчас показан — запускаем таймер исчезновения
                if (_currentShownFilePath == _lastFilePath)
                {
                    _leaveStartTime = now;
                }
                // Если уходим с файла, который ещё не был показан, — отменяем его показ
                if (_pendingFilePath == _lastFilePath)
                {
                    _pendingFilePath = null;
                }
            }

            _lastFilePath = filePath;

            // Курсор навился на новый файл
            if (filePath != null)
            {
                // Если вернулись на тот же файл, что показан — отменяем таймер исчезновения
                if (filePath == _currentShownFilePath)
                {
                    _leaveStartTime = DateTime.MinValue;
                }
                else
                {
                    // Новый файл — ставим его в ожидание и запоминаем время наведения
                    _hoverStartTime = now;
                    _pendingFilePath = filePath;
                }
            }
            else
            {
                // Ушли в пустоту — нет файла в ожидании
                _pendingFilePath = null;
            }
        }

        // 2. Проверка таймера исчезновения текущего окна
        if (_currentShownFilePath != null && _leaveStartTime != DateTime.MinValue)
        {
            if ((now - _leaveStartTime).TotalMilliseconds >= SettingsManager.Current.HideDelayMs)
            {
                _previewWindow?.HidePreview();
                _currentShownFilePath = null;
                _leaveStartTime = DateTime.MinValue;
            }
        }

        // 3. Если окно пустое и есть файл в ожидании — проверяем задержку появления
        if (_currentShownFilePath == null && _pendingFilePath != null)
        {
            // Убедимся, что курсор всё ещё на этом файле
            if (filePath == _pendingFilePath)
            {
                if ((now - _hoverStartTime).TotalMilliseconds >= SettingsManager.Current.HoverDelayMs)
                {
                    // Проверка размера
                    try
                    {
                        var fi = new System.IO.FileInfo(_pendingFilePath);
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
                // Курсор ушёл с ожидаемого файла — отменяем
                _pendingFilePath = null;
            }
        }
    }
}