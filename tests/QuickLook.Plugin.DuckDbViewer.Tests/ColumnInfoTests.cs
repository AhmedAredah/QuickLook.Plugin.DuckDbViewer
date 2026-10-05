using QuickLook.Plugin.DuckDbViewer.Data;
using Xunit;

namespace QuickLook.Plugin.DuckDbViewer.Tests;

public sealed class ColumnInfoTests
{
    [Theory]
    [InlineData("INTEGER", true)]
    [InlineData("BIGINT", true)]
    [InlineData("UTINYINT", true)]
    [InlineData("HUGEINT", true)]
    [InlineData("DOUBLE", true)]
    [InlineData("FLOAT", true)]
    [InlineData("DECIMAL(18,2)", true)]
    [InlineData("integer", true)]
    [InlineData("REAL", true)]
    [InlineData("VARCHAR", false)]
    [InlineData("INTEGER[]", false)]
    [InlineData("STRUCT(a INTEGER)", false)]
    [InlineData("TIMESTAMP", false)]
    [InlineData("INTERVAL", false)]
    [InlineData("", false)]
    public void Recognises_numeric_types(string type, bool expected)
    {
        Assert.Equal(expected, new ColumnInfo("c", type, null).IsNumeric);
    }
}
