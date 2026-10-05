using System;
using System.Collections.Generic;
using System.Linq;

namespace DuckDbViewer.Data;

/// <summary>
/// Which rows of a <see cref="DataObject"/> are wanted and in what order. Immutable: every
/// change produces a new instance, so a query handed to a background read cannot change
/// underneath it.
/// </summary>
internal sealed class RowQuery
{
    public static readonly RowQuery All = new([], null);

    private RowQuery(IReadOnlyList<ColumnFilter> filters, SortOrder? sort)
    {
        Filters = filters;
        Sort = sort;
    }

    /// <summary>At most one filter per column; a row must pass all of them.</summary>
    public IReadOnlyList<ColumnFilter> Filters { get; }

    public SortOrder? Sort { get; }

    public bool HasFilters => Filters.Count > 0;

    public ColumnFilter? FilterOn(string column) => Filters.FirstOrDefault(f => f.Column == column);

    public RowQuery WithSort(SortOrder? sort) => new(Filters, sort);

    /// <summary>Adds the filter, replacing an existing one on the same column.</summary>
    public RowQuery WithFilter(ColumnFilter filter)
    {
        return new RowQuery([.. Filters.Where(f => f.Column != filter.Column), filter], Sort);
    }

    public RowQuery WithoutFilter(string column)
    {
        return new RowQuery([.. Filters.Where(f => f.Column != column)], Sort);
    }

    public RowQuery WithoutFilters() => new([], Sort);
}

internal sealed class SortOrder
{
    public SortOrder(ColumnInfo column, bool descending)
    {
        Column = column;
        Descending = descending;
    }

    public ColumnInfo Column { get; }

    public bool Descending { get; }
}

/// <summary>
/// Keeps or drops rows by the value of one column. Values are compared as the text shown in
/// the grid; <c>null</c> stands for SQL NULL.
/// </summary>
/// <remarks>
/// Both directions exist because the list of distinct values offered to the user may be
/// incomplete for columns with very many values: "everything except these" must keep the
/// values that were never listed, which an include list could not express.
/// </remarks>
internal sealed class ColumnFilter
{
    private ColumnFilter(string column, IEnumerable<string?> values, bool isExclusion)
    {
        Column = column;
        Values = new HashSet<string?>(values, StringComparer.Ordinal);
        IsExclusion = isExclusion;
    }

    public string Column { get; }

    public IReadOnlyCollection<string?> Values { get; }

    /// <summary><c>true</c>: rows with these values are dropped. <c>false</c>: only they are kept.</summary>
    public bool IsExclusion { get; }

    public static ColumnFilter Include(string column, IEnumerable<string?> values) => new(column, values, false);

    public static ColumnFilter Exclude(string column, IEnumerable<string?> values) => new(column, values, true);

    /// <summary>Whether a row with this value passes the filter.</summary>
    public bool Accepts(string? value) => Values.Contains(value) != IsExclusion;
}
