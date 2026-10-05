using System;
using System.IO;

namespace DuckDbViewer.PreviewHandler;

/// <summary>
/// A small diagnostic log. The handler is started by COM without a console and is driven by
/// another process, so a file is the only place a failure can be seen.
/// </summary>
internal static class Log
{
    private const long MaxLength = 256 * 1024;

    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DuckDbViewer", "preview-handler.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxLength)
                    File.Delete(FilePath);

                File.AppendAllText(FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Logging must never break a preview.
        }
    }

    public static void Write(string context, Exception error) => Write($"{context}: {error}");
}
