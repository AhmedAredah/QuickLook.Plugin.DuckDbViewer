using System.Collections.Generic;

namespace DuckDbViewer.Data;

/// <summary>A distinct value of a column and the number of rows holding it.</summary>
internal sealed class ValueCount
{
    public ValueCount(string? value, long count)
    {
        Value = value;
        Count = count;
    }

    /// <summary>The value as display text; <c>null</c> for SQL NULL.</summary>
    public string? Value { get; }

    public long Count { get; }
}

/// <summary>The most frequent distinct values of a column.</summary>
internal sealed class ColumnValues
{
    public ColumnValues(IReadOnlyList<ValueCount> values, bool isTruncated)
    {
        Values = values;
        IsTruncated = isTruncated;
    }

    /// <summary>Ordered from most to least frequent.</summary>
    public IReadOnlyList<ValueCount> Values { get; }

    /// <summary>Whether the column has more distinct values than were returned.</summary>
    public bool IsTruncated { get; }
}
