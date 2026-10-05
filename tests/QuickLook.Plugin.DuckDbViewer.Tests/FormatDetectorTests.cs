using System.IO;
using System.Text;
using QuickLook.Plugin.DuckDbViewer.Detection;
using Xunit;

namespace QuickLook.Plugin.DuckDbViewer.Tests;

public sealed class FormatDetectorTests : IClassFixture<SampleFiles>
{
    private readonly SampleFiles _files;

    public FormatDetectorTests(SampleFiles files) => _files = files;

    [Fact]
    public void Detects_each_sample_by_content()
    {
        Assert.Equal(FileFormat.Parquet, FormatDetector.Detect(_files.Parquet));
        Assert.Equal(FileFormat.DuckDb, FormatDetector.Detect(_files.DuckDb));
        Assert.Equal(FileFormat.Sqlite, FormatDetector.Detect(_files.Sqlite));
    }

    [Fact]
    public void Generic_db_extension_is_resolved_by_content()
    {
        Assert.Equal(FileFormat.Sqlite, FormatDetector.Detect(_files.CopyAs(_files.Sqlite, "from-sqlite.db")));
        Assert.Equal(FileFormat.DuckDb, FormatDetector.Detect(_files.CopyAs(_files.DuckDb, "from-duckdb.db")));
    }

    [Fact]
    public void Extension_matching_ignores_case()
    {
        Assert.Equal(FileFormat.Parquet, FormatDetector.Detect(_files.CopyAs(_files.Parquet, "UPPER.PARQUET")));
    }

    [Fact]
    public void Unsupported_extension_is_ignored_even_with_matching_content()
    {
        Assert.Equal(FileFormat.Unknown, FormatDetector.Detect(_files.CopyAs(_files.Parquet, "data.bin")));
    }

    [Fact]
    public void Supported_extension_with_foreign_content_is_rejected()
    {
        Assert.Equal(FileFormat.Unknown, FormatDetector.Detect(_files.WriteText("notes.db", "just some text, long enough")));
        Assert.Equal(FileFormat.Unknown, FormatDetector.Detect(_files.WriteText("empty.parquet", string.Empty)));
    }

    [Fact]
    public void Missing_file_is_unknown()
    {
        Assert.Equal(FileFormat.Unknown, FormatDetector.Detect(Path.Combine(_files.Directory, "missing.parquet")));
    }

    [Theory]
    [InlineData("PAR1............", nameof(FileFormat.Parquet))]
    [InlineData("SQLite format 3\0", nameof(FileFormat.Sqlite))]
    [InlineData("12345678DUCK....", nameof(FileFormat.DuckDb))]
    [InlineData("DUCK............", nameof(FileFormat.Unknown))]
    [InlineData("SQLite format 2\0", nameof(FileFormat.Unknown))]
    [InlineData("PAR", nameof(FileFormat.Unknown))]
    [InlineData("", nameof(FileFormat.Unknown))]
    public void Detects_from_header(string header, string expected)
    {
        var bytes = Encoding.ASCII.GetBytes(header);
        var buffer = new byte[FormatDetector.HeaderLength];
        bytes.CopyTo(buffer, 0);

        Assert.Equal(expected, FormatDetector.DetectFromHeader(buffer, bytes.Length).ToString());
    }
}
