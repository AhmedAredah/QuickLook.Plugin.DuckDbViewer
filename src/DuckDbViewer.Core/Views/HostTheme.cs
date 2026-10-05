using System.Windows;
using System.Windows.Media;

namespace DuckDbViewer.Views;

/// <summary>
/// The colours the panel takes from its host. Inside QuickLook these resources are supplied
/// by QuickLook's own theme, which is where the key names come from; any other host places
/// the dictionary returned by <see cref="Create"/> above the panel.
/// </summary>
internal static class HostTheme
{
    public const string TextKey = "WindowTextForeground";
    public const string SecondaryTextKey = "WindowTextForegroundAlternative";
    public const string BackgroundKey = "MainWindowBackgroundNoTransparent";

    /// <summary>Derives a light or dark set of colours from the host's background.</summary>
    public static ResourceDictionary Create(Color background)
    {
        var dark = IsDark(background);
        return new ResourceDictionary
        {
            [TextKey] = Brush(dark ? Color.FromRgb(0xEF, 0xEF, 0xEF) : Color.FromRgb(0x0E, 0x0E, 0x0E)),
            [SecondaryTextKey] = Brush(dark ? Color.FromRgb(0xD4, 0xD4, 0xD4) : Color.FromRgb(0x62, 0x62, 0x62)),
            [BackgroundKey] = Brush(background),
        };
    }

    public static bool IsDark(Color color) => (color.R * 299 + color.G * 587 + color.B * 114) / 1000 < 128;

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
