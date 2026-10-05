using System.Collections.Generic;
using System.Threading;
using QuickLook.Plugin.DuckDbViewer.Data;

namespace QuickLook.Plugin.DuckDbViewer.Formats;

/// <summary>A SQLite database, read through DuckDB's <c>sqlite_scanner</c> extension.</summary>
internal sealed class SqliteFormat : AttachedDatabaseFormat
{
    private const string AllVarcharSetting = "sqlite_all_varchar";

    protected override string AttachOptions => "TYPE sqlite";

    public override bool ReadsRowsAsText => true;

    protected override void Prepare(DuckSession session, CancellationToken cancellationToken)
    {
        DuckExtension.Sqlite.Load(session, cancellationToken);

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
}
