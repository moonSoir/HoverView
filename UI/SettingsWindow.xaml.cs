using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
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
        
        ChkAutoStart.IsChecked = s.AutoStart;
        TxtImageFormats.Text = string.Join(", ", s.ImageFormats);
        TxtVideoFormats.Text = string.Join(", ", s.VideoFormats);
        TxtAudioFormats.Text = string.Join(", ", s.AudioFormats);
        TxtTextFormats.Text = string.Join(", ", s.TextFormats);
        TxtMaxSize.Text = s.MaxFileSizeMb.ToString();
        TxtMaxCacheSize.Text = s.MaxCacheSize.ToString();
        
        SldAudioVolume.Value = s.AudioVolume;
        LblAudioVolume.Text = $"{s.AudioVolume}%";
        
        TxtVersion.Text = s.Version;
        TxtAuthor.Text = $"Автор: {s.Author}";
        TxtGitHub.Text = s.GitHubUrl;
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
            MessageBox.Show("Ширина должна быть от 100 до 2000.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!double.TryParse(TxtHeight.Text, out double h) || h < 100 || h > 2000)
        {
            MessageBox.Show("Высота должна быть от 100 до 2000.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(TxtHoverDelay.Text, out int hoverDelay) || hoverDelay < 0 || hoverDelay > 5000)
        {
            MessageBox.Show("Задержка наведения должна быть от 0 до 5000 мс.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(TxtHideDelay.Text, out int hideDelay) || hideDelay < 0 || hideDelay > 5000)
        {
            MessageBox.Show("Задержка исчезновения должна быть от 0 до 5000 мс.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(TxtMaxSize.Text, out int maxSize) || maxSize < 0)
        {
            MessageBox.Show("Максимальный размер должен быть 0 или больше.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(TxtMaxCacheSize.Text, out int maxCache) || maxCache < 1 || maxCache > 1000)
        {
            MessageBox.Show("Размер кэша должен быть от 1 до 1000.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!Regex.IsMatch(TxtVersion.Text, @"^\d+\.\d+\.\d+$"))
        {
            MessageBox.Show("Версия должна быть в формате x.x.x (например, 1.0.0).", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var s = SettingsManager.Current;
        s.WindowWidth = w;
        s.WindowHeight = h;
        s.AutoSizeToContent = ChkAutoSizeToContent.IsChecked ?? false;
        s.UseMinimalFrame = ChkUseMinimalFrame.IsChecked ?? false;
        s.HoverDelayMs = hoverDelay;
        s.HideDelayMs = hideDelay;
        s.AutoStart = ChkAutoStart.IsChecked ?? false;
        s.MaxFileSizeMb = maxSize;
        s.MaxCacheSize = maxCache;
        s.Version = TxtVersion.Text;
        s.AudioVolume = (int)SldAudioVolume.Value;

        s.ImageFormats = ParseFormats(TxtImageFormats.Text);
        s.VideoFormats = ParseFormats(TxtVideoFormats.Text);
        s.AudioFormats = ParseFormats(TxtAudioFormats.Text);
        s.TextFormats = ParseFormats(TxtTextFormats.Text);

        SettingsManager.Save();
        AutoStartManager.Apply(s.AutoStart);
        
        DialogResult = true;
        Close();
    }

    private List<string> ParseFormats(string input)
    {
        return input
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().ToLowerInvariant())
            .Where(x => x.StartsWith("."))
            .ToList();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}