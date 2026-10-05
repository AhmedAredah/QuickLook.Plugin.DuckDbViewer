using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DuckDbViewer.Views;
using Microsoft.Win32;

namespace DuckDbViewer.PreviewHandler;

/// <summary>
/// The visual tree shown for one file: the shared preview panel, dressed in the host's
/// colours. Knows nothing about the COM protocol around it.
/// </summary>
internal sealed class PreviewContent : IDisposable
{
    private static readonly Color LightBackground = Color.FromRgb(0xF3, 0xF3, 0xF3);
    private static readonly Color DarkBackground = Color.FromRgb(0x20, 0x20, 0x20);

    private readonly Border _root;
    private readonly ViewerPanel _panel;

    public PreviewContent(string path)
    {
        _panel = new ViewerPanel();
        _root = new Border { Child = _panel, UseLayoutRounding = true };
        _ = _panel.Open(path, () => { });
    }

    public FrameworkElement Root => _root;

    /// <summary>Adopts the host's background colour, or the system theme when it gave none.</summary>
    public void ApplyTheme(Color? hostBackground)
    {
        var background = hostBackground ?? (SystemUsesLightTheme() ? LightBackground : DarkBackground);
        _root.Resources = HostTheme.Create(background);
        _root.Background = new SolidColorBrush(background);
    }

    public void Dispose() => _panel.Dispose();

    private static bool SystemUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }
}
