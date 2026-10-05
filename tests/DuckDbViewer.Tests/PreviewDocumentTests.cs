using System;
using System.IO;
using System.Linq;
using System.Threading;
using DuckDbViewer.Data;
using DuckDbViewer.Detection;
using Xunit;

namespace DuckDbViewer.Tests;

public sealed class PreviewDocumentTests : IClassFixture<SampleFiles>
{
    private readonly SampleFiles _files;

    public PreviewDocumentTests(SampleFiles files) => _files = files;

    // ---- Parquet -------------------------------------------------------------------------

    [Fact]
    public void Parquet_is_a_single_table_named_after_the_file()
    {
        using var document = PreviewDocument.Open(_files.Parquet);

        Assert.Equal(FileFormat.Parquet, document.Format);
        var table = Assert.Single(document.Objects);
        Assert.Equal("sample", table.DisplayName);
        Assert.Equal(DataObjectKind.Table, table.Kind);
        Assert.Equal(SampleFiles.ParquetRowCount, document.CountRows(table, RowQuery.All));
    }

    [Fact]
    public void Parquet_schema_reports_names_and_types()
    {
        using var document = PreviewDocument.Open(_files.Parquet);

        var columns = document.GetColumns(document.Objects[0]);

        Assert.Equal(
            new[] { "id", "full.name", "score", "nested", "notes", "seen_at", "amount" },
            columns.Select(c => c.Name));
        Assert.Equal("BIGINT", columns[0].Type);
        Assert.Equal("STRUCT(a BIGINT, b BIGINT[])", columns[3].Type);
        Assert.Equal("TIMESTAMP WITH TIME ZONE", columns[5].Type);
        Assert.Equal("DECIMAL(18,2)", columns[6].Type);
        Assert.True(columns[0].IsNumeric);
        Assert.False(columns[1].IsNumeric);
    }

    [Fact]
    public void Pages_honour_offset_and_limit()
    {
        using var document = PreviewDocument.Open(_files.Parquet);
        var table = document.Objects[0];

        var page = document.ReadPage(table, RowQuery.All, offset: 1000, limit: 500);

        Assert.Equal(SampleFiles.ParquetRowCount - 1000, page.Count);
        Assert.Equal("1000", page[0][0]);
        Assert.Equal("name 1000", page[0][1]);
        Assert.Equal((SampleFiles.ParquetRowCount - 1).ToString(), page[page.Count - 1][0]);
        Assert.Empty(document.ReadPage(table, RowQuery.All, offset: 5000, limit: 500));
    }

    [Fact]
    public void Cells_are_rendered_as_text_with_nulls_preserved()
    {
        using var document = PreviewDocument.Open(_files.Parquet);

        var page = document.ReadPage(document.Objects[0], RowQuery.All, offset: 0, limit: 2);

        Assert.Equal("0.0", page[0][2]);
        Assert.Null(page[1][2]);
        Assert.Equal("{'a': 1, 'b': [1, 2]}", page[1][3]);
        Assert.Equal("100.00", page[1][6]);
        Assert.StartsWith("2024-01-", page[1][5]);
    }

    [Fact]
    public void Long_cells_are_truncated_with_an_ellipsis()
    {
        using var document = PreviewDocument.Open(_files.Parquet);

        var page = document.ReadPage(document.Objects[0], RowQuery.All, offset: 0, limit: 2);

        Assert.Equal(TableReader.MaxCellLength + 1, page[0][4]!.Length);
        Assert.EndsWith("…", page[0][4]);
        Assert.Equal("short", page[1][4]);
    }

    // ---- DuckDB --------------------------------------------------------------------------

    [Fact]
    public void DuckDb_lists_tables_and_views_of_every_schema()
    {
        using var document = PreviewDocument.Open(_files.DuckDb);

        Assert.Equal(FileFormat.DuckDb, document.Format);
        Assert.Equal(
            new[] { "alpha", "alpha_labels", "beta", SampleFiles.OddTableName, "other.gamma" },
            document.Objects.Select(o => o.DisplayName).OrderBy(n => n, StringComparer.Ordinal));
        Assert.True(document.Objects.Single(o => o.DisplayName == "alpha_labels").IsView);
        Assert.False(document.Objects.Single(o => o.DisplayName == "alpha").IsView);
    }

    [Fact]
    public void DuckDb_objects_are_readable()
    {
        using var document = PreviewDocument.Open(_files.DuckDb);

        foreach (var source in document.Objects)
        {
            var columns = document.GetColumns(source);
            var rows = document.ReadPage(source, RowQuery.All, 0, 100);

            Assert.NotEmpty(columns);
            Assert.Equal(document.CountRows(source, RowQuery.All), rows.Count);
            Assert.All(rows, row => Assert.Equal(columns.Count, row.Length));
        }

        var gamma = document.Objects.Single(o => o.DisplayName == "other.gamma");
        Assert.Equal("42", document.ReadPage(gamma, RowQuery.All, 0, 10)[0][0]);

        var beta = document.Objects.Single(o => o.DisplayName == "beta");
        Assert.False(document.GetColumns(beta)[0].IsNullable);
        Assert.True(document.GetColumns(beta)[1].IsNullable);
    }

    // ---- SQLite --------------------------------------------------------------------------

    [Fact]
    public void Sqlite_lists_tables_and_views()
    {
        using var document = PreviewDocument.Open(_files.Sqlite);

        Assert.Equal(FileFormat.Sqlite, document.Format);
        Assert.Equal(
            new[] { "adults", SampleFiles.OddTableName, "numbers", "people" },
            document.Objects.Select(o => o.DisplayName).OrderBy(n => n, StringComparer.Ordinal));
        Assert.True(document.Objects.Single(o => o.DisplayName == "adults").IsView);
    }

    [Fact]
    public void Sqlite_schema_reports_types_even_though_rows_are_read_as_text()
    {
        using var document = PreviewDocument.Open(_files.Sqlite);
        var people = document.Objects.Single(o => o.DisplayName == "people");

        var columns = document.GetColumns(people);

        Assert.Equal(new[] { "id", "born", "score" }, columns.Select(c => c.Name));
        Assert.Equal(new[] { "BIGINT", "VARCHAR", "DOUBLE" }, columns.Select(c => c.Type));

        // Looking up the schema must not switch row reading back to typed mode.
        Assert.Equal("not a number", document.ReadPage(people, RowQuery.All, 0, 100)[2][0]);
    }

    [Fact]
    public void Sqlite_values_that_do_not_match_the_declared_type_are_still_shown()
    {
        using var document = PreviewDocument.Open(_files.Sqlite);
        var people = document.Objects.Single(o => o.DisplayName == "people");

        var rows = document.ReadPage(people, RowQuery.All, 0, 100);

        Assert.Equal(3, document.CountRows(people, RowQuery.All));
        Assert.Equal(new[] { "1", "2020-01-31", "1.5" }, rows[0]);
        Assert.Equal(new string?[] { "2", null, null }, rows[1]);
        Assert.Equal(new[] { "not a number", "not a date", "n/a" }, rows[2]);
    }

    [Fact]
    public void Sqlite_objects_with_unusual_names_are_readable()
    {
        using var document = PreviewDocument.Open(_files.Sqlite);
        var odd = document.Objects.Single(o => o.DisplayName == SampleFiles.OddTableName);

        Assert.Equal("a b", Assert.Single(document.GetColumns(odd)).Name);
        Assert.Empty(document.ReadPage(odd, RowQuery.All, 0, 10));

        var view = document.Objects.Single(o => o.DisplayName == "adults");
        Assert.Equal("id", Assert.Single(document.GetColumns(view)).Name);
        Assert.Equal(3, document.ReadPage(view, RowQuery.All, 0, 10).Count);
    }

    // ---- Behaviour shared by all formats ---------------------------------------------------

    [Fact]
    public void Previewing_never_modifies_the_file_and_releases_it_afterwards()
    {
        foreach (var original in new[] { _files.Parquet, _files.DuckDb, _files.Sqlite })
        {
            var copy = _files.CopyAs(original, "readonly-" + Path.GetFileName(original));
            var before = File.ReadAllBytes(copy);

            using (var document = PreviewDocument.Open(copy))
            {
                foreach (var source in document.Objects)
                {
                    document.GetColumns(source);
                    document.ReadPage(source, RowQuery.All, 0, 10);
                    document.CountRows(source, RowQuery.All);
                }
            }

            Assert.Equal(before, File.ReadAllBytes(copy));
            // No journal, WAL or temporary files may be left next to the previewed file.
            Assert.Equal(
                new[] { Path.GetFileName(copy) },
                Directory.GetFiles(_files.Directory, "readonly-*").Select(Path.GetFileName));
            File.Delete(copy);
            Assert.False(File.Exists(copy));
        }
    }

    [Fact]
    public void A_cancelled_request_does_not_run()
    {
        using var document = PreviewDocument.Open(_files.Parquet);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(
            () => document.ReadPage(document.Objects[0], RowQuery.All, 0, 10, cancellation.Token));

        // The document stays usable afterwards.
        Assert.Equal(10, document.ReadPage(document.Objects[0], RowQuery.All, 0, 10).Count);
    }

    [Fact]
    public void Unsupported_files_are_rejected()
    {
        var path = _files.WriteText("notes.db", "just some text, long enough");

        Assert.Throws<InvalidDataException>(() => PreviewDocument.Open(path));
    }

    [Fact]
    public void Paths_with_quotes_and_spaces_are_supported()
    {
        var path = _files.CopyAs(_files.Parquet, "o'brien & co (final).parquet");

        using var document = PreviewDocument.Open(path);

        Assert.Equal(SampleFiles.ParquetRowCount, document.CountRows(document.Objects[0], RowQuery.All));
    }
}
