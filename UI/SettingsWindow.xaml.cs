using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;

namespace HoverView.UI;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        LoadSettingsToUI();

        ChkAutoSizeToContent.Checked += (s, e) => ChkUseMinimalFrame.IsChecked = false;
        ChkUseMinimalFrame.Checked += (s, e) => ChkAutoSizeToContent.IsChecked = false;

        ChkPauseOnFullScreenApp.Checked += (s, e) => FullScreenModePanel.IsEnabled = true;
        ChkPauseOnFullScreenApp.Unchecked += (s, e) => FullScreenModePanel.IsEnabled = false;

        SldAudioVolume.ValueChanged += (s, e) =>
        {
            LblAudioVolume.Text = $"{(int)SldAudioVolume.Value}%";
        };
    }

    private void LoadSettingsToUI()
    {
        var s = SettingsManager.Current;

        TxtWidth.Text = s.WindowWidth.ToString("0");
        TxtHeight.Text = s.WindowHeight.ToString("0");

        ChkAutoSizeToContent.IsChecked = s.AutoSizeToContent;
        ChkUseMinimalFrame.IsChecked = s.UseMinimalFrame;

        TxtHoverDelay.Text = s.HoverDelayMs.ToString();
        TxtHideDelay.Text = s.HideDelayMs.ToString();

        ChkPauseOnFullScreenApp.IsChecked = s.PauseOnFullScreenApp;
        FullScreenModePanel.IsEnabled = s.PauseOnFullScreenApp;
        CmbFullScreenMode.SelectedIndex = (int)s.FullScreenMode;

        ChkExpandArchiveTree.IsChecked = s.ExpandArchiveTree;

        ChkAutoStart.IsChecked = s.AutoStart;

        TxtImageFormats.Text = string.Join(", ", s.ImageFormats);
        TxtVideoFormats.Text = string.Join(", ", s.VideoFormats);
        TxtAudioFormats.Text = string.Join(", ", s.AudioFormats);
        TxtTextFormats.Text = string.Join(", ", s.TextFormats);
        TxtArchiveFormats.Text = string.Join(", ", s.ArchiveFormats);

        TxtMaxSize.Text = s.MaxFileSizeMb.ToString();
        TxtMaxArchiveEntries.Text = s.MaxArchiveEntries.ToString();
        TxtMaxTextLines.Text = s.MaxTextLines.ToString();
        TxtMaxCacheSize.Text = s.MaxCacheSize.ToString();
        TxtMaxCacheSizeMb.Text = s.MaxCacheSizeMb.ToString();

        SldAudioVolume.Value = s.AudioVolume;
        LblAudioVolume.Text = $"{s.AudioVolume}%";

        foreach (ComboBoxItem item in CmbLanguage.Items)
        {
            if ((string)item.Tag == s.Language)
            {
                CmbLanguage.SelectedItem = item;
                break;
            }
        }

        TxtVersion.Text = s.Version;
        TxtAuthor.Text = string.Format(Localization.Get("Author"), s.Author);
    }

    private void TxtGitHub_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var url = SettingsManager.Current.GitHubUrl;
            if (!string.IsNullOrWhiteSpace(url))
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch { }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(TxtWidth.Text, out double w) || w < 100 || w > 2000)
        {
            MessageBox.Show(Localization.Get("ErrorWidth"), Localization.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!double.TryParse(TxtHeight.Text, out double h) || h < 100 || h > 2000)
        {
            MessageBox.Show(Localization.Get("ErrorHeight"), Localization.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(TxtHoverDelay.Text, out int hoverDelay) || hoverDelay < 0 || hoverDelay > 5000)
        {
            MessageBox.Show(Localization.Get("ErrorHoverDelay"), Localization.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(TxtHideDelay.Text, out int hideDelay) || hideDelay < 0 || hideDelay > 5000)
        {
            MessageBox.Show(Localization.Get("ErrorHideDelay"), Localization.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(TxtMaxSize.Text, out int maxSize) || maxSize < 0)
        {
            MessageBox.Show(Localization.Get("ErrorMaxSize"), Localization.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(TxtMaxArchiveEntries.Text, out int maxArchiveEntries) || maxArchiveEntries < 0)
        {
            MessageBox.Show(Localization.Get("ErrorMaxArchiveEntries"), Localization.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(TxtMaxTextLines.Text, out int maxTextLines) || maxTextLines < 0)
        {
            MessageBox.Show(Localization.Get("ErrorMaxTextLines"), Localization.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(TxtMaxCacheSize.Text, out int maxCache) || maxCache < 1 || maxCache > 1000)
        {
            MessageBox.Show(Localization.Get("ErrorMaxCacheSize"), Localization.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(TxtMaxCacheSizeMb.Text, out int maxCacheMb) || maxCacheMb < 1 || maxCacheMb > 10000)
        {
            MessageBox.Show(Localization.Get("ErrorMaxCacheSizeMb"), Localization.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var s = SettingsManager.Current;
        string oldLanguage = s.Language;

        s.WindowWidth = w;
        s.WindowHeight = h;
        s.AutoSizeToContent = ChkAutoSizeToContent.IsChecked ?? false;
        s.UseMinimalFrame = ChkUseMinimalFrame.IsChecked ?? false;
        s.HoverDelayMs = hoverDelay;
        s.HideDelayMs = hideDelay;
        s.PauseOnFullScreenApp = ChkPauseOnFullScreenApp.IsChecked ?? true;
        s.FullScreenMode = (FullScreenCheckMode)CmbFullScreenMode.SelectedIndex;
        s.ExpandArchiveTree = ChkExpandArchiveTree.IsChecked ?? true;
        s.AutoStart = ChkAutoStart.IsChecked ?? false;
        s.MaxFileSizeMb = maxSize;
        s.MaxArchiveEntries = maxArchiveEntries;
        s.MaxTextLines = maxTextLines;
        s.MaxCacheSize = maxCache;
        s.MaxCacheSizeMb = maxCacheMb;
        s.AudioVolume = (int)SldAudioVolume.Value;

        if (CmbLanguage.SelectedItem is ComboBoxItem langItem)
        {
            s.Language = (string)langItem.Tag;
        }

        s.ImageFormats = ParseFormats(TxtImageFormats.Text);
        s.VideoFormats = ParseFormats(TxtVideoFormats.Text);
        s.AudioFormats = ParseFormats(TxtAudioFormats.Text);
        s.TextFormats = ParseFormats(TxtTextFormats.Text);
        s.ArchiveFormats = ParseFormats(TxtArchiveFormats.Text);

        SettingsManager.Save();
        AutoStartManager.Apply(s.AutoStart);

        Localization.SetLanguage(s.Language);

        // Обновляем меню трея сразу (System.Windows.Application, чтобы не путать с WinForms)
        if (System.Windows.Application.Current is App app)
        {
            app.RefreshTrayMenu();
        }

        if (oldLanguage != s.Language)
        {
            var newWindow = new SettingsWindow();
            newWindow.Show();
            this.Close();
            return;
        }

        Title = Localization.Get("SettingsTitle") + " " + Localization.Get("Saved");
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (s2, e2) =>
        {
            Title = Localization.Get("SettingsTitle");
            timer.Stop();
        };
        timer.Start();
    }

    private List<string> ParseFormats(string input)
    {
        return input
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().ToLowerInvariant())
            .Where(x => x.StartsWith("."))
            .ToList();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
