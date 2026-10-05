using System.Collections.Generic;
using System.IO;
using System.Threading;
using QuickLook.Plugin.DuckDbViewer.Data;

namespace QuickLook.Plugin.DuckDbViewer.Formats;

/// <summary>A Parquet file is a single table.</summary>
internal sealed class ParquetFormat : FormatHandler
{
    public override IReadOnlyList<DataObject> Open(DuckSession session, string path,
        CancellationToken cancellationToken)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return
        [
            new DataObject(name, DataObjectKind.Table, $"read_parquet({SqlText.Literal(path)})", name),
        ];
    }
}
