using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DuckDbViewer.Data;
using DuckDbViewer.Detection;
using DuckDbViewer.ViewModels;
using Xunit;

namespace DuckDbViewer.Tests;

/// <summary>
/// Avro and Arrow files, read from samples written by fastavro and pyarrow (see
/// <c>Fixtures\generate.py</c>). Every sample holds the same 250 rows.
/// </summary>
public sealed class AvroAndArrowTests : IDisposable
{
    private const int RowCount = 250;

    private readonly string _workDirectory =
        Path.Combine(Path.GetTempPath(), "DuckDbViewerTests-" + Guid.NewGuid().ToString("N"));

    public AvroAndArrowTests() => Directory.CreateDirectory(_workDirectory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    private static string Fixture(string name)
    {
        return Path.Combine(Path.GetDirectoryName(typeof(AvroAndArrowTests).Assembly.Location)!, "Fixtures", name);
    }

    // ---- Detection -------------------------------------------------------------------------

    [Theory]
    [InlineData("null.avro", nameof(FileFormat.Avro))]
    [InlineData("deflate.avro", nameof(FileFormat.Avro))]
    [InlineData("file.arrow", nameof(FileFormat.Arrow))]
    [InlineData("plain.feather", nameof(FileFormat.Arrow))]
    [InlineData("zstd.feather", nameof(FileFormat.Arrow))]
    [InlineData("stream.arrows", nameof(FileFormat.Arrow))]
    public void Samples_are_detected_by_content(string fixture, string expected)
    {
        Assert.Equal(expected, FormatDetector.Detect(Fixture(fixture)).ToString());
    }

    [Fact]
    public void An_arrow_stream_is_only_trusted_under_an_arrow_extension()
    {
        // A stream starts with a bare 0xFFFFFFFF marker, which other binary files may share.
        var asArrow = Path.Combine(_workDirectory, "renamed.arrow");
        var asDatabase = Path.Combine(_workDirectory, "renamed.db");
        File.Copy(Fixture("stream.arrows"), asArrow);
        File.Copy(Fixture("stream.arrows"), asDatabase);

        Assert.Equal(FileFormat.Arrow, FormatDetector.Detect(asArrow));
        Assert.Equal(FileFormat.Unknown, FormatDetector.Detect(asDatabase));
    }

    [Fact]
    public void Legacy_feather_version_1_is_not_claimed()
    {
        var path = Path.Combine(_workDirectory, "legacy.feather");
        File.WriteAllBytes(path, System.Text.Encoding.ASCII.GetBytes("FEA1 followed by a version 1 body"));

        Assert.Equal(FileFormat.Unknown, FormatDetector.Detect(path));
    }

    // ---- Avro ------------------------------------------------------------------------------

    [Theory]
    [InlineData("null.avro")]
    [InlineData("deflate.avro")]
    [InlineData("snappy.avro")]
    public void Avro_files_of_every_common_codec_are_readable(string fixture)
    {
        using var document = PreviewDocument.Open(Fixture(fixture));
        var table = Assert.Single(document.Objects);

        Assert.Equal(FileFormat.Avro, document.Format);
        Assert.Equal(Path.GetFileNameWithoutExtension(fixture), table.DisplayName);
        Assert.Equal(
            new[] { "id:BIGINT", "city:VARCHAR", "score:DOUBLE", "tags:INTEGER[]" },
            document.GetColumns(table).Select(c => $"{c.Name}:{c.Type}"));
        Assert.Equal(RowCount, document.CountRows(table, RowQuery.All));

        var rows = document.ReadPage(table, RowQuery.All, 0, 10);
        Assert.Equal(new[] { "0", "Austin", null, "[0, 0]" }, rows[0]);
        Assert.Equal(new[] { "2", null, "3.0", "[2, 2]" }, rows[2]);
    }

    // ---- Arrow and Feather -----------------------------------------------------------------

    [Theory]
    [InlineData("file.arrow")]
    [InlineData("stream.arrows")]
    [InlineData("plain.feather")]
    [InlineData("zstd.feather")]
    public void Arrow_files_streams_and_feather_files_are_readable(string fixture)
    {
        using var document = PreviewDocument.Open(Fixture(fixture));
        var table = Assert.Single(document.Objects);

        Assert.Equal(FileFormat.Arrow, document.Format);
        Assert.Equal(
            new[] { "id:BIGINT", "city:VARCHAR", "score:DOUBLE", "seen:TIMESTAMP", "tags:INTEGER[]" },
            document.GetColumns(table).Select(c => $"{c.Name}:{c.Type}"));
        Assert.Equal(RowCount, document.CountRows(table, RowQuery.All));

        var rows = document.ReadPage(table, RowQuery.All, 0, 10);
        Assert.Equal(new[] { "1", "Houston", "1.5", "2025-01-01 00:01:00", "[1, 1]" }, rows[1]);
        Assert.Null(rows[2][1]);
    }

    [Fact]
    public void Lz4_compressed_feather_is_reported_in_plain_words()
    {
        using var document = PreviewDocument.Open(Fixture("lz4.feather"));
        var table = document.Objects[0];

        var error = Assert.Throws<InvalidDataException>(() => document.ReadPage(table, RowQuery.All, 0, 10));

        Assert.Contains("LZ4", error.Message);
        Assert.DoesNotContain("ArrowIpcDecoder", error.Message);
    }

    [Fact]
    public async Task Lz4_compressed_feather_shows_a_notice_in_the_panel()
    {
        using var viewModel = new ViewerViewModel();

        await viewModel.OpenAsync(Fixture("lz4.feather"));

        Assert.True(viewModel.HasNotice);
        Assert.Contains("LZ4", viewModel.NoticeDetail);
    }

    // ---- Shared features work for the new formats --------------------------------------------

    [Theory]
    [InlineData("deflate.avro")]
    [InlineData("zstd.feather")]
    public void Sorting_filtering_and_distinct_values_work(string fixture)
    {
        using var document = PreviewDocument.Open(Fixture(fixture));
        var table = document.Objects[0];
        var id = document.GetColumns(table).Single(c => c.Name == "id");
        var query = RowQuery.All
            .WithFilter(ColumnFilter.Include("city", [null, "Dallas"]))
            .WithSort(new SortOrder(id, descending: true));

        var rows = document.ReadPage(table, query, 0, 3);
        var cities = document.GetDistinctValues(table, "city", RowQuery.All);

        Assert.Equal(new[] { "247", "246", "243" }, rows.Select(r => r[0]));
        Assert.Equal(124, document.CountRows(table, query));
        Assert.Equal(
            new (string?, long)[] { ("Austin", 63), ("Houston", 63), (null, 62), ("Dallas", 62) },
            cities.Values.Select(v => (v.Value, v.Count)));
    }

    [Theory]
    [InlineData("snappy.avro", "Avro")]
    [InlineData("file.arrow", "Arrow")]
    public async Task The_panel_opens_them_and_can_export_to_parquet(string fixture, string formatName)
    {
        var copy = Path.Combine(_workDirectory, fixture);
        File.Copy(Fixture(fixture), copy);
        using var viewModel = new ViewerViewModel();

        await viewModel.OpenAsync(copy);
        await viewModel.ExportAsync(ExportFormat.Parquet);

        Assert.Equal(formatName, viewModel.FormatName);
        Assert.False(viewModel.HasObjectList);
        Assert.Equal(RowCount, viewModel.Page.TotalRows);
        Assert.Equal(Path.ChangeExtension(copy, ".parquet"), viewModel.ExportedPath);

        using var exported = PreviewDocument.Open(viewModel.ExportedPath!);
        Assert.Equal(RowCount, exported.CountRows(exported.Objects[0], RowQuery.All));
    }
}
