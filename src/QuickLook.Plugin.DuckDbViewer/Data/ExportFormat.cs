using System;

namespace QuickLook.Plugin.DuckDbViewer.Data;

internal enum ExportFormat
{
    Csv,
    Parquet,
    Json,
}

internal static class ExportFormatExtensions
{
    /// <summary>File extension including the dot.</summary>
    public static string Extension(this ExportFormat format) => format switch
    {
        ExportFormat.Csv => ".csv",
        ExportFormat.Parquet => ".parquet",
        ExportFormat.Json => ".json",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    public static string DisplayName(this ExportFormat format) => format switch
    {
        ExportFormat.Csv => "CSV",
        ExportFormat.Parquet => "Parquet",
        ExportFormat.Json => "JSON",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    /// <summary>The options of DuckDB's <c>COPY ... TO</c> statement.</summary>
    internal static string CopyOptions(this ExportFormat format) => format switch
    {
        ExportFormat.Csv => "FORMAT csv, HEADER true",
        ExportFormat.Parquet => "FORMAT parquet",
        // One JSON array rather than newline-delimited objects, which more tools accept.
        ExportFormat.Json => "FORMAT json, ARRAY true",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };
}
