namespace QuickLook.Plugin.DuckDbViewer.Formats;

/// <summary>An Apache Avro object container file.</summary>
internal sealed class AvroFormat : TableFileFormat
{
    protected override string ReadFunction => "read_avro";

    protected override DuckExtension Extension => DuckExtension.Avro;
}
