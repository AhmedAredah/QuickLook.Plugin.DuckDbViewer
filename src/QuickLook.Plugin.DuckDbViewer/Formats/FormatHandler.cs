using System;
using System.Collections.Generic;
using System.Threading;
using QuickLook.Plugin.DuckDbViewer.Data;
using QuickLook.Plugin.DuckDbViewer.Detection;

namespace QuickLook.Plugin.DuckDbViewer.Formats;

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
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "No handler for this format."),
    };

    /// <summary>
    /// Whether every value of this format arrives as text regardless of the column type.
    /// Sorting then has to restore numeric order itself.
    /// </summary>
    public virtual bool ReadsRowsAsText => false;

    /// <summary>Makes the file readable in <paramref name="session"/> and lists its objects.</summary>
    public abstract IReadOnlyList<DataObject> Open(DuckSession session, string path,
        CancellationToken cancellationToken);

    public virtual IReadOnlyList<ColumnInfo> DescribeColumns(DuckSession session, DataObject source,
        CancellationToken cancellationToken)
    {
        return TableReader.DescribeColumns(session, source, cancellationToken);
    }
}
