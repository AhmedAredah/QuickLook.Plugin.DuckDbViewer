using System.IO;
using System.Threading;
using QuickLook.Plugin.DuckDbViewer.Data;
using QuickLook.Plugin.DuckDbViewer.Native;
using DuckDBException = DuckDB.NET.Data.DuckDBException;

namespace QuickLook.Plugin.DuckDbViewer.Formats;

/// <summary>
/// A DuckDB extension a format depends on. Keep the list in sync with the
/// <c>BundledExtension</c> items of the project file.
/// </summary>
internal sealed class DuckExtension
{
    public static readonly DuckExtension Sqlite = new("sqlite_scanner", isCommunity: false);
    public static readonly DuckExtension Avro = new("avro", isCommunity: false);
    public static readonly DuckExtension Arrow = new("nanoarrow", isCommunity: true);

    private DuckExtension(string name, bool isCommunity)
    {
        Name = name;
        IsCommunity = isCommunity;
    }

    public string Name { get; }

    /// <summary>Whether the extension is published in DuckDB's community repository.</summary>
    public bool IsCommunity { get; }

    /// <summary>
    /// Loads the extension into <paramref name="session"/>. The copy shipped with the plugin is
    /// preferred so that previewing works offline. A package without bundled extensions falls
    /// back to DuckDB's own extension store, which downloads the extension once.
    /// </summary>
    public void Load(DuckSession session, CancellationToken cancellationToken)
    {
        var bundled = Path.Combine(PluginEnvironment.BaseDirectory, "extensions", Name + ".duckdb_extension");
        if (File.Exists(bundled))
        {
            session.Execute($"LOAD {SqlText.Literal(bundled)}", cancellationToken);
            return;
        }

        try
        {
            session.Execute($"LOAD {Name}", cancellationToken);
        }
        catch (DuckDBException)
        {
            session.Execute(IsCommunity ? $"INSTALL {Name} FROM community" : $"INSTALL {Name}", cancellationToken);
            session.Execute($"LOAD {Name}", cancellationToken);
        }
    }
}
