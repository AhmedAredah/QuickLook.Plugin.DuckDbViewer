using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace QuickLook.Plugin.DuckDbViewer.Data;

/// <summary>
/// Format-independent queries against a <see cref="DataObject"/>.
/// </summary>
internal static class TableReader
{
    /// <summary>Longer cell values are cut off and end with an ellipsis.</summary>
    public const int MaxCellLength = 1000;

    public static List<ColumnInfo> DescribeColumns(DuckSession session, DataObject source,
        CancellationToken cancellationToken = default)
    {
        return session.Query(
            $"DESCRIBE SELECT * FROM {source.SourceSql}",
            row => new ColumnInfo(
                row.GetString(0),
                row.GetString(1),
                row.IsDBNull(2) ? null : string.Equals(row.GetString(2), "YES", StringComparison.OrdinalIgnoreCase)),
            cancellationToken);
    }

    public static long CountRows(DuckSession session, DataObject source,
        CancellationToken cancellationToken = default)
    {
        var count = session.Scalar($"SELECT count(*) FROM {source.SourceSql}", cancellationToken);
        return Convert.ToInt64(count, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads one page of rows as display text; a <c>null</c> cell is a SQL NULL.
    /// </summary>
    /// <remarks>
    /// Values are converted to text by DuckDB itself. That gives every type, including nested
    /// and exotic ones, a faithful rendering without mapping each to a .NET type.
    /// </remarks>
    public static List<string?[]> ReadPage(DuckSession session, DataObject source, long offset, int limit,
        CancellationToken cancellationToken = default)
    {
        const string text = "COLUMNS(*)::VARCHAR";
        var max = MaxCellLength.ToString(CultureInfo.InvariantCulture);
        var sql =
            $"SELECT CASE WHEN length({text}) > {max} THEN left({text}, {max}) || '…' ELSE {text} END " +
            $"FROM {source.SourceSql} " +
            $"LIMIT {limit.ToString(CultureInfo.InvariantCulture)} OFFSET {offset.ToString(CultureInfo.InvariantCulture)}";

        return session.Query(sql, row =>
        {
            var cells = new string?[row.FieldCount];
            for (var i = 0; i < cells.Length; i++)
                cells[i] = row.IsDBNull(i) ? null : row.GetString(i);
            return cells;
        }, cancellationToken);
    }
}
