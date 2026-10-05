namespace DuckDbViewer.Data;

internal enum DataObjectKind
{
    Table,
    View,
}

/// <summary>
/// Something that can be shown as a grid: a table or view of a database, or a whole data file.
/// </summary>
internal sealed class DataObject
{
    public DataObject(string displayName, DataObjectKind kind, string sourceSql, string name)
    {
        DisplayName = displayName;
        Kind = kind;
        SourceSql = sourceSql;
        Name = name;
    }

    /// <summary>The name shown to the user.</summary>
    public string DisplayName { get; }

    public DataObjectKind Kind { get; }

    /// <summary>An already escaped expression usable after <c>FROM</c>.</summary>
    public string SourceSql { get; }

    /// <summary>The unqualified, unescaped object name inside the file.</summary>
    public string Name { get; }

    public bool IsView => Kind == DataObjectKind.View;

    public override string ToString() => DisplayName;
}
