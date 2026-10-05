using System;
using System.IO;
using System.Linq;
using System.Threading;
using DuckDbViewer.Data;
using DuckDbViewer.ViewModels;
using Xunit;

namespace DuckDbViewer.Tests;

public sealed class ExportTests : IClassFixture<SampleFiles>
{
    private readonly SampleFiles _files;

    public ExportTests(SampleFiles files) => _files = files;

    [Fact]
    public void Csv_export_writes_a_header_and_every_row()
    {
        var target = Target("all.csv");
        using (var document = PreviewDocument.Open(_files.Sales))
            document.Export(document.Objects[0], RowQuery.All, target, ExportFormat.Csv);

        var lines = File.ReadAllLines(target);

        Assert.Equal("region,amount,the note", lines[0]);
        Assert.Equal(11, lines.Length);
        Assert.Equal("north,5,first", lines[1]);
    }

    [Fact]
    public void Export_honours_filters_and_sort_order()
    {
        var target = Target("filtered.csv");
        using (var document = PreviewDocument.Open(_files.Sales))
        {
            var source = document.Objects[0];
            var amount = document.GetColumns(source).Single(c => c.Name == "amount");
            var query = RowQuery.All
                .WithFilter(ColumnFilter.Include("region", ["north"]))
                .WithSort(new SortOrder(amount, descending: true));

            document.Export(source, query, target, ExportFormat.Csv);
        }

        Assert.Equal(
            new[] { "region,amount,the note", "north,30,g", "north,9,c", "north,5,first" },
            File.ReadAllLines(target));
    }

    [Fact]
    public void Parquet_export_keeps_column_types_and_can_be_previewed_again()
    {
        var target = Target("from-duckdb.parquet");
        using (var document = PreviewDocument.Open(_files.DuckDb))
        {
            var alpha = document.Objects.Single(o => o.DisplayName == "alpha");
            document.Export(alpha, RowQuery.All, target, ExportFormat.Parquet);
        }

        using var exported = PreviewDocument.Open(target);
        var table = exported.Objects[0];

        Assert.Equal(new[] { "BIGINT", "VARCHAR" }, exported.GetColumns(table).Select(c => c.Type));
        Assert.Equal(3, exported.CountRows(table, RowQuery.All));
    }

    [Fact]
    public void Json_export_writes_one_array()
    {
        var target = Target("all.json");
        using (var document = PreviewDocument.Open(_files.Sales))
            document.Export(document.Objects[0], RowQuery.All, target, ExportFormat.Json);

        var json = File.ReadAllText(target).Trim();

        Assert.StartsWith("[", json);
        Assert.EndsWith("]", json);
        Assert.Contains("\"region\":\"north\"", json.Replace(" ", string.Empty));
    }

    [Fact]
    public void Sqlite_tables_can_be_exported()
    {
        var target = Target("people.csv");
        using (var document = PreviewDocument.Open(_files.Sqlite))
        {
            var people = document.Objects.Single(o => o.DisplayName == "people");
            document.Export(people, RowQuery.All, target, ExportFormat.Csv);
        }

        Assert.Equal(4, File.ReadAllLines(target).Length);
    }

    [Fact]
    public void A_failed_or_cancelled_export_leaves_no_file_and_keeps_the_document_usable()
    {
        using var document = PreviewDocument.Open(_files.Sales);
        var source = document.Objects[0];

        var unreachable = Path.Combine(_files.Directory, "missing-folder", "out.csv");
        Assert.ThrowsAny<Exception>(() => document.Export(source, RowQuery.All, unreachable, ExportFormat.Csv));
        Assert.False(File.Exists(unreachable));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = Target("cancelled.csv");
        Assert.ThrowsAny<OperationCanceledException>(
            () => document.Export(source, RowQuery.All, cancelled, ExportFormat.Csv, cancellation.Token));
        Assert.False(File.Exists(cancelled));

        Assert.Equal(10, document.CountRows(source, RowQuery.All));
    }

    [Fact]
    public void Export_never_modifies_the_source_file()
    {
        var before = File.ReadAllBytes(_files.DuckDb);
        using (var document = PreviewDocument.Open(_files.DuckDb))
        {
            foreach (var source in document.Objects)
                document.Export(source, RowQuery.All, Target($"copy-{Guid.NewGuid():N}.csv"), ExportFormat.Csv);
        }

        Assert.Equal(before, File.ReadAllBytes(_files.DuckDb));
    }

    // ---- Choosing the destination ----------------------------------------------------------

    [Fact]
    public void Target_is_next_to_the_source_and_named_after_file_and_object()
    {
        var source = Path.Combine(_files.Directory, "shop.duckdb");

        Assert.Equal(
            Path.Combine(_files.Directory, "shop_orders.csv"),
            ExportTarget.Choose(source, "orders", ExportFormat.Csv));
        Assert.Equal(
            Path.Combine(_files.Directory, "shop.parquet"),
            ExportTarget.Choose(source, null, ExportFormat.Parquet));
    }

    [Fact]
    public void Target_never_overwrites_an_existing_file()
    {
        var folder = Path.Combine(_files.Directory, "unique-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "data.csv"), string.Empty);
        File.WriteAllText(Path.Combine(folder, "data (2).csv"), string.Empty);

        Assert.Equal(Path.Combine(folder, "data (3).csv"), ExportTarget.Unique(folder, "data", ".csv"));
        Assert.Equal(Path.Combine(folder, "other.csv"), ExportTarget.Unique(folder, "other", ".csv"));
    }

    [Theory]
    [InlineData("orders", "orders")]
    [InlineData("a/b:c*d", "a_b_c_d")]
    [InlineData("it's \"odd\"", "it's _odd_")]
    [InlineData("...", "export")]
    public void Object_names_are_made_safe_for_file_names(string name, string expected)
    {
        Assert.Equal(expected, ExportTarget.Sanitize(name));
    }

    private string Target(string name) => Path.Combine(_files.Directory, name);
}
