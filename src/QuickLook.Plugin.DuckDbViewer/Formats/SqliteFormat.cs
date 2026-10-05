using System.Collections.Generic;
using System.IO;
using System.Threading;
using QuickLook.Plugin.DuckDbViewer.Data;
using QuickLook.Plugin.DuckDbViewer.Native;
using DuckDBException = DuckDB.NET.Data.DuckDBException;

namespace QuickLook.Plugin.DuckDbViewer.Formats;

/// <summary>A SQLite database, read through DuckDB's <c>sqlite_scanner</c> extension.</summary>
internal sealed class SqliteFormat : AttachedDatabaseFormat
{
    private const string ExtensionName = "sqlite_scanner";
    private const string AllVarcharSetting = "sqlite_all_varchar";

    protected override string AttachOptions => "TYPE sqlite";

    public override bool ReadsRowsAsText => true;

    protected override void Prepare(DuckSession session, CancellationToken cancellationToken)
    {
        LoadExtension(session, cancellationToken);

        // SQLite does not enforce column types, so a column declared INTEGER may hold text.
        // Reading everything as text avoids conversion errors on such rows.
        session.Execute($"SET {AllVarcharSetting} = true", cancellationToken);
    }

    /// <summary>
    /// Reports the column types as DuckDB maps them, which requires switching the
    /// all-text mode off for the duration of the lookup.
    /// </summary>
    public override IReadOnlyList<ColumnInfo> DescribeColumns(DuckSession session, DataObject source,
        CancellationToken cancellationToken)
    {
        return session.Exclusive(() =>
        {
            session.Execute($"SET {AllVarcharSetting} = false", cancellationToken);
            try
            {
                return base.DescribeColumns(session, source, cancellationToken);
            }
            finally
            {
                // Must be restored even when the lookup was cancelled.
                session.Execute($"SET {AllVarcharSetting} = true", CancellationToken.None);
            }
        });
    }

    /// <summary>
    /// Prefers the extension shipped with the plugin so that previewing works offline. A build
    /// without the bundled binary falls back to DuckDB's own extension store, which downloads
    /// the extension once from the official repository.
    /// </summary>
    private static void LoadExtension(DuckSession session, CancellationToken cancellationToken)
    {
        var bundled = Path.Combine(PluginEnvironment.BaseDirectory, "extensions",
            ExtensionName + ".duckdb_extension");
        if (File.Exists(bundled))
        {
            session.Execute($"LOAD {SqlText.Literal(bundled)}", cancellationToken);
            return;
        }

        try
        {
            session.Execute($"LOAD {ExtensionName}", cancellationToken);
        }
        catch (DuckDBException)
        {
            session.Execute($"INSTALL {ExtensionName}", cancellationToken);
            session.Execute($"LOAD {ExtensionName}", cancellationToken);
        }
    }
}
