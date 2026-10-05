using System;
using System.IO;
using System.Linq;
using QuickLook.Plugin.DuckDbViewer.Data;

namespace QuickLook.Plugin.DuckDbViewer.ViewModels;

/// <summary>
/// Decides where an export is written: next to the previewed file when that folder is
/// writable, otherwise in the user's Downloads folder. Existing files are never overwritten.
/// </summary>
internal static class ExportTarget
{
    /// <param name="sourcePath">The previewed file.</param>
    /// <param name="objectName">The exported table or view, or <c>null</c> for a single-table file.</param>
    public static string Choose(string sourcePath, string? objectName, ExportFormat format)
    {
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        if (!string.IsNullOrEmpty(objectName))
            baseName += "_" + Sanitize(objectName!);

        var directory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        if (!IsWritable(directory))
            directory = DownloadsDirectory();

        return Unique(directory, baseName, format.Extension());
    }

    internal static string Unique(string directory, string baseName, string extension)
    {
        var candidate = Path.Combine(directory, baseName + extension);
        for (var number = 2; File.Exists(candidate) || Directory.Exists(candidate); number++)
            candidate = Path.Combine(directory, $"{baseName} ({number}){extension}");

        return candidate;
    }

    internal static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        return cleaned.Length == 0 ? "export" : cleaned;
    }

    private static bool IsWritable(string directory)
    {
        var probe = Path.Combine(directory, ".duckdbviewer-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string DownloadsDirectory()
    {
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(downloads);
        return downloads;
    }
}
