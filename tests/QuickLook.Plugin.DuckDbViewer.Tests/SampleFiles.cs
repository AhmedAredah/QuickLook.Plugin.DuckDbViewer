using System;
using System.IO;
using DuckDB.NET.Data;
using QuickLook.Plugin.DuckDbViewer.Data;
using QuickLook.Plugin.DuckDbViewer.Native;

namespace QuickLook.Plugin.DuckDbViewer.Tests;

/// <summary>
/// Creates one small file of every supported format in a temporary folder. The files are
/// written with DuckDB directly, independently of the code under test.
/// </summary>
public sealed class SampleFiles : IDisposable
{
    public const int ParquetRowCount = 1234;
    public const string OddTableName = "it's \"odd\"";

    public SampleFiles()
    {
        Directory = Path.Combine(Path.GetTempPath(), "DuckDbViewerTests-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);

        Parquet = Path.Combine(Directory, "sample.parquet");
        DuckDb = Path.Combine(Directory, "sample.duckdb");
        Sqlite = Path.Combine(Directory, "sample.sqlite");

        CreateParquet();
        CreateDuckDb();
        CreateSqlite();
    }

    public string Directory { get; }

    public string Parquet { get; }

    public string DuckDb { get; }

    public string Sqlite { get; }

    /// <summary>Copies a sample under a new name, for tests that need a specific extension.</summary>
    public string CopyAs(string source, string fileName)
    {
        var target = Path.Combine(Directory, fileName);
        File.Copy(source, target, overwrite: true);
        return target;
    }

    public string WriteText(string fileName, string content)
    {
        var target = Path.Combine(Directory, fileName);
        File.WriteAllText(target, content);
        return target;
    }

    private void CreateParquet()
    {
        Run(
            "COPY (SELECT " +
            "  i AS id, " +
            "  'name ' || i AS \"full.name\", " +
            "  CASE WHEN i % 2 = 0 THEN i * 1.5 END AS score, " +
            "  {'a': i, 'b': [i, i + 1]} AS nested, " +
            "  CASE WHEN i = 0 THEN repeat('x', 5000) ELSE 'short' END AS notes, " +
            "  TIMESTAMPTZ '2024-01-02 03:04:05+00' AS seen_at, " +
            "  (i * 100)::DECIMAL(18, 2) AS amount " +
            $"FROM range({ParquetRowCount}) t(i)) TO {SqlText.Literal(Parquet)} (FORMAT parquet)");
    }

    private void CreateDuckDb()
    {
        Run(
            $"ATTACH {SqlText.Literal(DuckDb)} AS target",
            "CREATE TABLE target.alpha AS SELECT i AS id, 'row ' || i AS label FROM range(3) t(i)",
            "CREATE TABLE target.beta (id INTEGER NOT NULL, note VARCHAR)",
            // Created the way users do it: from inside the database, with unqualified names.
            "USE target",
            "CREATE VIEW alpha_labels AS SELECT label FROM alpha",
            "USE memory",
            "CREATE SCHEMA target.other",
            "CREATE TABLE target.other.gamma AS SELECT 42 AS answer",
            $"CREATE TABLE target.{SqlText.Identifier(OddTableName)} AS SELECT 1 AS \"a b\"",
            "DETACH target");
    }

    private void CreateSqlite()
    {
        var extension = Path.Combine(PluginEnvironment.BaseDirectory, "extensions", "sqlite_scanner.duckdb_extension");
        Run(
            $"LOAD {SqlText.Literal(extension)}",
            $"ATTACH {SqlText.Literal(Sqlite)} AS target (TYPE sqlite)",
            "CREATE TABLE target.people (id INTEGER NOT NULL, born DATE, score REAL)",
            "INSERT INTO target.people VALUES (1, DATE '2020-01-31', 1.5), (2, NULL, NULL)",
            // A SQLite view may only reference objects of its own database, unqualified.
            "USE target",
            "CREATE VIEW adults AS SELECT id FROM people",
            "USE memory",
            $"CREATE TABLE target.{SqlText.Identifier(OddTableName)} (\"a b\" TEXT)",
            "DETACH target",
            // SQLite does not enforce types; store text in the INTEGER column like real files do.
            "SET sqlite_all_varchar = true",
            $"ATTACH {SqlText.Literal(Sqlite)} AS target (TYPE sqlite)",
            "INSERT INTO target.people VALUES ('not a number', 'not a date', 'n/a')",
            "DETACH target");
    }

    private static void Run(params string[] statements)
    {
        using var connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();
        foreach (var sql in statements)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; a leaked handle is reported by the dedicated test.
        }
    }
}
