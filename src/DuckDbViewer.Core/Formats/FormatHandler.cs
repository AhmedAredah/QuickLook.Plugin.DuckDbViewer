using System;
using System.Collections.Generic;
using System.Threading;
using DuckDbViewer.Data;
using DuckDbViewer.Detection;

namespace DuckDbViewer.Formats;

/// <summary>
/// Knows how to expose one file format as a list of <see cref="DataObject"/>s. Everything
/// after that (schema, counting, paging) is shared, so supporting a new format means adding
/// one subclass and registering it in <see cref="For"/>.
/// </summary>
internal abstract class FormatHandler
{
    public static FormatHandler For(FileFormat format) => format switch
    {
        FileFormat.Parquet => new ParquetFormat(),
        FileFormat.DuckDb => new DuckDbFormat(),
        FileFormat.Sqlite => new SqliteFormat(),
        FileFormat.Avro => new AvroFormat(),
        FileFormat.Arrow => new ArrowFormat(),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "No handler for this format."),
    };

    /// <summary>
    /// Whether every value of this format arrives as text regardless of the column type.
    /// Sorting then has to restore numeric order itself.
    /// </summary>
    public virtual bool ReadsRowsAsText => false;

    /// <summary>
    /// Translates an engine error that users of this format commonly run into. Returns
    /// <c>null</c> to show the engine's own message.
    /// </summary>
    public virtual string? ExplainError(string message) => null;

    /// <summary>Makes the file readable in <paramref name="session"/> and lists its objects.</summary>
    public abstract IReadOnlyList<DataObject> Open(DuckSession session, string path,
        CancellationToken cancellationToken);

    public virtual IReadOnlyList<ColumnInfo> DescribeColumns(DuckSession session, DataObject source,
        CancellationToken cancellationToken)
    {
        return TableReader.DescribeColumns(session, source, cancellationToken);
    }
}
