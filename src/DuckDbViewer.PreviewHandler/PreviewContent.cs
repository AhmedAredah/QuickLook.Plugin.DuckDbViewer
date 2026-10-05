using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DuckDbViewer.PreviewHandler;

/// <summary>The visual tree shown for one file, independent of the COM protocol around it.</summary>
internal interface IPreviewContent : IDisposable
{
    FrameworkElement Root { get; }

    /// <summary>Adopts the host's background colour, or the system theme when it gave none.</summary>
    void ApplyTheme(Color? hostBackground);
}

internal static class PreviewContent
{
    // SPIKE: placeholder content proving that the host launches and embeds the handler.
    public static IPreviewContent Create(string path) => new Placeholder(path);

    private sealed class Placeholder : IPreviewContent
    {
        private readonly Border _root;
        private readonly TextBlock _text;

        public Placeholder(string path)
        {
            _text = new TextBlock
            {
                Text = $"DuckDbViewer preview handler\n{path}\n.NET {Environment.Version}, process {Environment.ProcessId}",
                FontSize = 18,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(24),
            };
            _root = new Border { Child = _text };
        }

        public FrameworkElement Root => _root;

        public void ApplyTheme(Color? hostBackground)
        {
            var background = hostBackground ?? Colors.White;
            var isDark = (background.R * 299 + background.G * 587 + background.B * 114) / 1000 < 128;
            _root.Background = new SolidColorBrush(background);
            _text.Foreground = isDark ? Brushes.White : Brushes.Black;
        }

        public void Dispose()
        {
        }
    }
}
