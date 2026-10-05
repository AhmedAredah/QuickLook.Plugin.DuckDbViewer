using DuckDbViewer.Data;
using Xunit;

namespace DuckDbViewer.Tests;

public sealed class SqlTextTests
{
    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("with space", "\"with space\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("", "\"\"")]
    public void Identifier_doubles_double_quotes(string name, string expected)
    {
        Assert.Equal(expected, SqlText.Identifier(name));
    }

    [Theory]
    [InlineData("plain", "'plain'")]
    [InlineData("it's", "'it''s'")]
    [InlineData(@"C:\data\o'brien.db", @"'C:\data\o''brien.db'")]
    public void Literal_doubles_single_quotes(string value, string expected)
    {
        Assert.Equal(expected, SqlText.Literal(value));
    }
}
