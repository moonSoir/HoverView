using Microsoft.Win32;
using System;
using System.IO;

namespace HoverView;

public static class AutoStartManager
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "HoverView";

    public static void Apply(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue(AppName, $"\"{exePath}\"");
            }
            else
            {
                if (key.GetValue(AppName) != null)
                    key.DeleteValue(AppName);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                string.Format(Localization.Get("ErrorAutoStartMessage"), ex.Message),
                Localization.Get("ErrorAutoStartTitle"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }
}
