using System.Collections.Generic;
using System.IO;
using System.Threading;
using DuckDbViewer.Data;

namespace DuckDbViewer.Formats;

/// <summary>
/// Base for files that hold exactly one table and are read through a DuckDB table function
/// such as <c>read_parquet</c>.
/// </summary>
internal abstract class TableFileFormat : FormatHandler
{
    /// <summary>The DuckDB table function that reads the file, taking its path.</summary>
    protected abstract string ReadFunction { get; }

    /// <summary>The extension providing <see cref="ReadFunction"/>, if it is not built in.</summary>
    protected virtual DuckExtension? Extension => null;

    public override IReadOnlyList<DataObject> Open(DuckSession session, string path,
        CancellationToken cancellationToken)
    {
        Extension?.Load(session, cancellationToken);

        var name = Path.GetFileNameWithoutExtension(path);
        return
        [
            new DataObject(name, DataObjectKind.Table, $"{ReadFunction}({SqlText.Literal(path)})", name),
        ];
    }
}
