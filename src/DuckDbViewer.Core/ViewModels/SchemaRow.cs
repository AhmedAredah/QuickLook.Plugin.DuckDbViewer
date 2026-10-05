using DuckDbViewer.Data;
using DuckDbViewer.Localization;

namespace DuckDbViewer.ViewModels;

/// <summary>One line of the schema tab.</summary>
internal sealed class SchemaRow
{
    public SchemaRow(int position, ColumnInfo column)
    {
        Position = position;
        Name = column.Name;
        Type = column.Type;
        Nullable = column.IsNullable switch
        {
            true => Strings.Get("Schema_Yes"),
            false => Strings.Get("Schema_No"),
            null => string.Empty,
        };
    }

    public int Position { get; }

    public string Name { get; }

    public string Type { get; }

    public string Nullable { get; }
}
