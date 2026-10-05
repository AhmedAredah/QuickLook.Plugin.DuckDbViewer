using System.IO;

namespace DuckDbViewer;

/// <summary>Where the viewer's own files (extensions, translations) are installed.</summary>
internal static class AppEnvironment
{
    /// <summary>
    /// The folder this assembly was loaded from. Every host ships the shared files next to it:
    /// the QuickLook plugin folder, or the folder of the preview handler executable.
    /// </summary>
    public static string BaseDirectory { get; } =
        Path.GetDirectoryName(typeof(AppEnvironment).Assembly.Location)!;
}
