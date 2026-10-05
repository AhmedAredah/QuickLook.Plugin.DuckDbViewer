namespace DuckDbViewer.Formats;

/// <summary>An Apache Parquet file.</summary>
internal sealed class ParquetFormat : TableFileFormat
{
    protected override string ReadFunction => "read_parquet";
}
