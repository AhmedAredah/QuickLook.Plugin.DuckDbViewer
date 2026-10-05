using System;
using System.Collections.Generic;
using System.Threading;
using QuickLook.Plugin.DuckDbViewer.Data;

namespace QuickLook.Plugin.DuckDbViewer.Formats;

/// <summary>
/// Base for formats that DuckDB can <c>ATTACH</c> as a catalog. The file is always attached
/// read-only so previewing can never modify it.
/// </summary>
internal abstract class AttachedDatabaseFormat : FormatHandler
{
    /// <summary>The catalog name the previewed file is attached under.</summary>
    protected const string Catalog = "preview";

    private const string DefaultSchema = "main";

    /// <summary>Extra <c>ATTACH</c> options, for example <c>TYPE sqlite</c>.</summary>
    protected virtual string? AttachOptions => null;

    /// <summary>Runs before the file is attached, for example to load an extension.</summary>
    protected virtual void Prepare(DuckSession session, CancellationToken cancellationToken)
    {
    }

    public override IReadOnlyList<DataObject> Open(DuckSession session, string path,
        CancellationToken cancellationToken)
    {
        Prepare(session, cancellationToken);

        var options = AttachOptions is null ? "READ_ONLY" : $"{AttachOptions}, READ_ONLY";
        session.Execute(
            $"ATTACH {SqlText.Literal(path)} AS {SqlText.Identifier(Catalog)} ({options})",
            cancellationToken);

        // Views store their query with unqualified names, which only resolve when the
        // previewed file is the default catalog.
        session.Execute($"USE {SqlText.Identifier(Catalog)}", cancellationToken);

        var catalog = SqlText.Literal(Catalog);
        var sql =
            "SELECT schema_name, table_name, false AS is_view FROM duckdb_tables() " +
            $"WHERE database_name = {catalog} AND NOT internal " +
            "UNION ALL " +
            "SELECT schema_name, view_name, true FROM duckdb_views() " +
            $"WHERE database_name = {catalog} AND NOT internal " +
            "ORDER BY 1, 2";

        return session.Query(sql, row =>
        {
            var schema = row.GetString(0);
            var name = row.GetString(1);
            var kind = row.GetBoolean(2) ? DataObjectKind.View : DataObjectKind.Table;
            var isDefaultSchema = string.Equals(schema, DefaultSchema, StringComparison.OrdinalIgnoreCase);

            return new DataObject(
                isDefaultSchema ? name : $"{schema}.{name}",
                kind,
                $"{SqlText.Identifier(Catalog)}.{SqlText.Identifier(schema)}.{SqlText.Identifier(name)}",
                name);
        }, cancellationToken);
    }
}
