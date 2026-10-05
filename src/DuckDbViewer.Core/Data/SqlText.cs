namespace DuckDbViewer.Data;

/// <summary>
/// Escaping helpers for the SQL the plugin generates. Table, schema and file names come from
/// the previewed file and must never be concatenated into a statement unescaped.
/// </summary>
internal static class SqlText
{
    /// <summary>Quotes a name for use as an identifier: <c>my"table</c> becomes <c>"my""table"</c>.</summary>
    public static string Identifier(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    /// <summary>Quotes a value for use as a string literal: <c>it's</c> becomes <c>'it''s'</c>.</summary>
    public static string Literal(string value) => "'" + value.Replace("'", "''") + "'";
}
