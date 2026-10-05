using System;
using DuckDbViewer.Localization;

namespace DuckDbViewer.Formats;

/// <summary>
/// An Apache Arrow IPC file or stream. Feather version 2 files are Arrow IPC files under
/// another name.
/// </summary>
internal sealed class ArrowFormat : TableFileFormat
{
    protected override string ReadFunction => "read_arrow";

    protected override DuckExtension Extension => DuckExtension.Arrow;

    /// <summary>
    /// The Arrow reader available for this DuckDB version cannot decode LZ4-compressed record
    /// batches, which is what pandas and pyarrow write to Feather files by default. The engine
    /// reports that as an internal decoder failure; say what it means instead.
    /// </summary>
    public override string? ExplainError(string message)
    {
        return message.IndexOf("ArrowIpcDecoderDecodeArray", StringComparison.Ordinal) >= 0
            ? Strings.Get("Error_ArrowCompression")
            : null;
    }
}
