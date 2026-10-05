using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DuckDbViewer.Detection;

/// <summary>
/// Decides whether the plugin can preview a file. The extension only gates which files are
/// inspected; the actual format always comes from the file's magic bytes, because generic
/// extensions such as <c>.db</c> are shared by several database engines.
/// </summary>
internal static class FormatDetector
{
    internal const int HeaderLength = 16;

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".parquet", ".parq",
        ".duckdb", ".ddb",
        ".sqlite", ".sqlite3", ".db3", ".s3db", ".sl3", ".sqlitedb",
        ".db",

        // Application formats that are SQLite databases underneath.
        ".gpkg",    // OGC GeoPackage
        ".mbtiles", // Mapbox tile sets

        ".avro",
        ".arrow", ".arrows", ".feather", ".ipc",
    };

    /// <summary>
    /// Extensions for which an Arrow IPC <em>stream</em> is accepted. A stream has no magic
    /// string, only a marker that is too weak to trust under an unrelated extension.
    /// </summary>
    private static readonly HashSet<string> ArrowStreamExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".arrow", ".arrows", ".ipc",
    };

    private static readonly byte[] ParquetMagic = Encoding.ASCII.GetBytes("PAR1");
    private static readonly byte[] SqliteMagic = Encoding.ASCII.GetBytes("SQLite format 3\0");
    private static readonly byte[] DuckDbMagic = Encoding.ASCII.GetBytes("DUCK");
    private const int DuckDbMagicOffset = 8;
    private static readonly byte[] AvroMagic = [(byte)'O', (byte)'b', (byte)'j', 1];

    // Arrow IPC file format, which is also Feather version 2. Feather version 1 ("FEA1") is a
    // different, long-deprecated layout that the engine cannot read.
    private static readonly byte[] ArrowFileMagic = Encoding.ASCII.GetBytes("ARROW1");
    private static readonly byte[] ArrowStreamMarker = [0xFF, 0xFF, 0xFF, 0xFF];

    public static IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public static FileFormat Detect(string path)
    {
        var extension = Path.GetExtension(path);
        if (!Extensions.Contains(extension))
            return FileFormat.Unknown;

        try
        {
            // Share generously: the file may be open in the application that owns it.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var header = new byte[HeaderLength];
            var read = stream.Read(header, 0, header.Length);

            var format = DetectFromHeader(header, read);
            if (format == FileFormat.Unknown && ArrowStreamExtensions.Contains(extension) &&
                StartsWith(header, read, 0, ArrowStreamMarker))
                return FileFormat.Arrow;

            return format;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Unreadable files are left to QuickLook's default handler.
            return FileFormat.Unknown;
        }
    }

    /// <summary>Recognises the formats that announce themselves with a magic string.</summary>
    internal static FileFormat DetectFromHeader(byte[] header, int length)
    {
        if (StartsWith(header, length, 0, ParquetMagic))
            return FileFormat.Parquet;
        if (StartsWith(header, length, 0, SqliteMagic))
            return FileFormat.Sqlite;
        if (StartsWith(header, length, DuckDbMagicOffset, DuckDbMagic))
            return FileFormat.DuckDb;
        if (StartsWith(header, length, 0, AvroMagic))
            return FileFormat.Avro;
        if (StartsWith(header, length, 0, ArrowFileMagic))
            return FileFormat.Arrow;

        return FileFormat.Unknown;
    }

    private static bool StartsWith(byte[] header, int length, int offset, byte[] magic)
    {
        if (length < offset + magic.Length)
            return false;

        for (var i = 0; i < magic.Length; i++)
        {
            if (header[offset + i] != magic[i])
                return false;
        }

        return true;
    }
}
