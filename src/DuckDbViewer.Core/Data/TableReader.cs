using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace DuckDbViewer.Data;

/// <summary>
/// Format-independent queries against a <see cref="DataObject"/>. This is the only place
/// where row-level SQL is written.
/// </summary>
internal static class TableReader
{
    /// <summary>Longer cell values are cut off and end with an ellipsis.</summary>
    public const int MaxCellLength = 1000;

    /// <summary>How many distinct values of a column are offered for filtering.</summary>
    public const int MaxDistinctValues = 1000;

    /// <summary>
    /// Filters compare only the beginning of a value. This bounds the size of the value list
    /// for columns holding documents or blobs while staying consistent: the same prefix is
    /// used to list values and to match rows.
    /// </summary>
    public const int FilterKeyLength = 200;

    // Every statement names the source "t" and qualifies columns with it, so a reference can
    // never be mistaken for an output column of the same name.
    private const string Alias = "t";

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

    public static long CountRows(DuckSession session, DataObject source, RowQuery query,
        CancellationToken cancellationToken = default)
    {
        var count = session.Scalar($"SELECT count(*) {From(source)}{Where(query.Filters)}", cancellationToken);
        return Convert.ToInt64(count, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads one page of rows as display text; a <c>null</c> cell is a SQL NULL.
    /// </summary>
    /// <remarks>
    /// Values are converted to text by DuckDB itself. That gives every type, including nested
    /// and exotic ones, a faithful rendering without mapping each to a .NET type.
    /// </remarks>
    public static List<string?[]> ReadPage(DuckSession session, DataObject source, RowQuery query,
        bool rowsAreText, long offset, int limit, CancellationToken cancellationToken = default)
    {
        const string text = "COLUMNS(*)::VARCHAR";
        var max = MaxCellLength.ToString(CultureInfo.InvariantCulture);
        var sql =
            $"SELECT CASE WHEN length({text}) > {max} THEN left({text}, {max}) || '…' ELSE {text} END " +
            From(source) + Where(query.Filters) + OrderBy(query.Sort, rowsAreText) +
            $" LIMIT {limit.ToString(CultureInfo.InvariantCulture)} OFFSET {offset.ToString(CultureInfo.InvariantCulture)}";

        return session.Query(sql, row =>
        {
            var cells = new string?[row.FieldCount];
            for (var i = 0; i < cells.Length; i++)
                cells[i] = row.IsDBNull(i) ? null : row.GetString(i);
            return cells;
        }, cancellationToken);
    }

    /// <summary>
    /// Lists the most frequent values of <paramref name="column"/> among the rows that pass
    /// the filters on the <em>other</em> columns, so the choices narrow as filters are added
    /// while the column's own filter can still be widened again.
    /// </summary>
    public static ColumnValues DistinctValues(DuckSession session, DataObject source, string column,
        RowQuery query, CancellationToken cancellationToken = default)
    {
        var otherFilters = query.Filters.Where(f => f.Column != column);
        var sql =
            $"SELECT {TextOf(column)} AS value, count(*) AS row_count " +
            From(source) + Where(otherFilters) +
            " GROUP BY 1 ORDER BY 2 DESC, 1 NULLS FIRST" +
            $" LIMIT {(MaxDistinctValues + 1).ToString(CultureInfo.InvariantCulture)}";

        var values = session.Query(
            sql,
            row => new ValueCount(row.IsDBNull(0) ? null : row.GetString(0), Convert.ToInt64(row.GetValue(1))),
            cancellationToken);

        var isTruncated = values.Count > MaxDistinctValues;
        if (isTruncated)
            values.RemoveAt(values.Count - 1);

        return new ColumnValues(values, isTruncated);
    }

    /// <summary>Writes every row matching <paramref name="query"/>, in its order, to a new file.</summary>
    public static void Export(DuckSession session, DataObject source, RowQuery query, bool rowsAreText,
        string path, ExportFormat format, CancellationToken cancellationToken = default)
    {
        var select = $"SELECT * {From(source)}{Where(query.Filters)}{OrderBy(query.Sort, rowsAreText)}";
        session.Execute($"COPY ({select}) TO {SqlText.Literal(path)} ({format.CopyOptions()})", cancellationToken);
    }

    private static string From(DataObject source) => $"FROM {source.SourceSql} AS {Alias}";

    private static string Reference(string column) => $"{Alias}.{SqlText.Identifier(column)}";

    /// <summary>The text a filter compares: the displayed value, limited to <see cref="FilterKeyLength"/>.</summary>
    private static string TextOf(string column)
    {
        return $"left({Reference(column)}::VARCHAR, {FilterKeyLength.ToString(CultureInfo.InvariantCulture)})";
    }

    private static string Where(IEnumerable<ColumnFilter> filters)
    {
        var conditions = filters.Select(Condition).ToList();
        return conditions.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", conditions);
    }

    /// <summary>
    /// NULL needs its own test: it is never equal to anything, so <c>IN</c> would not select it
    /// and <c>NOT IN</c> would not keep it.
    /// </summary>
    private static string Condition(ColumnFilter filter)
    {
        var reference = Reference(filter.Column);
        var listsNull = filter.Values.Contains(null);
        var literals = string.Join(", ", filter.Values.Where(v => v is not null).Select(v => SqlText.Literal(v!)));

        if (filter.IsExclusion)
        {
            var nullTest = listsNull ? $"{reference} IS NOT NULL" : $"{reference} IS NULL";
            if (literals.Length == 0)
                return listsNull ? nullTest : "TRUE";

            var notListed = $"{TextOf(filter.Column)} NOT IN ({literals})";
            return listsNull ? $"({nullTest} AND {notListed})" : $"({nullTest} OR {notListed})";
        }

        if (literals.Length == 0)
            return listsNull ? $"{reference} IS NULL" : "FALSE";

        var listed = $"{TextOf(filter.Column)} IN ({literals})";
        return listsNull ? $"({reference} IS NULL OR {listed})" : listed;
    }

    /// <summary>
    /// Sorts on the column's own type. Where every value arrives as text (SQLite), numeric
    /// columns are ordered by their numeric value first so that 9 comes before 10.
    /// </summary>
    private static string OrderBy(SortOrder? sort, bool rowsAreText)
    {
        if (sort is null)
            return string.Empty;

        var direction = (sort.Descending ? "DESC" : "ASC") + " NULLS LAST";
        var reference = Reference(sort.Column.Name);
        var keys = rowsAreText && sort.Column.IsNumeric
            ? $"TRY_CAST({reference} AS DOUBLE) {direction}, {reference} {direction}"
            : $"{reference} {direction}";

        return " ORDER BY " + keys;
    }
}
