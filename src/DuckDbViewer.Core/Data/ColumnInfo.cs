using System.Text.RegularExpressions;

namespace DuckDbViewer.Data;

internal sealed class ColumnInfo
{
    private static readonly Regex NumericType = new(
        @"^(U?(TINY|SMALL|BIG|HUGE)?INT(EGER)?\d*|FLOAT\d*|DOUBLE( PRECISION)?|REAL|(DECIMAL|NUMERIC)(\(.*\))?)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public ColumnInfo(string name, string type, bool? isNullable)
    {
        Name = name;
        Type = type;
        IsNullable = isNullable;
        IsNumeric = NumericType.IsMatch(type.Trim());
    }

    public string Name { get; }

    /// <summary>The type as declared by the source, for example <c>DECIMAL(10,2)</c>.</summary>
    public string Type { get; }

    /// <summary><c>null</c> when the source does not say.</summary>
    public bool? IsNullable { get; }

    /// <summary>Whether values read naturally right-aligned.</summary>
    public bool IsNumeric { get; }
}
