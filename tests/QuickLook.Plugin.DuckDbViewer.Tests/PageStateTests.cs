using QuickLook.Plugin.DuckDbViewer.ViewModels;
using Xunit;

namespace QuickLook.Plugin.DuckDbViewer.Tests;

public sealed class PageStateTests
{
    [Fact]
    public void First_page_of_a_counted_table()
    {
        var page = new PageState(pageSize: 500, pageIndex: 0, rowsOnPage: 500, totalRows: 1234);

        Assert.Equal(1, page.FirstRow);
        Assert.Equal(500, page.LastRow);
        Assert.Equal(3, page.PageCount);
        Assert.False(page.CanGoPrevious);
        Assert.True(page.CanGoNext);
        Assert.True(page.CanGoLast);
    }

    [Fact]
    public void Last_partial_page()
    {
        var page = new PageState(pageSize: 500, pageIndex: 2, rowsOnPage: 234, totalRows: 1234);

        Assert.Equal(1001, page.FirstRow);
        Assert.Equal(1234, page.LastRow);
        Assert.True(page.CanGoPrevious);
        Assert.False(page.CanGoNext);
        Assert.False(page.CanGoLast);
    }

    [Fact]
    public void Total_that_is_an_exact_multiple_of_the_page_size_has_no_empty_last_page()
    {
        var page = new PageState(pageSize: 500, pageIndex: 1, rowsOnPage: 500, totalRows: 1000);

        Assert.Equal(2, page.PageCount);
        Assert.False(page.CanGoNext);
    }

    [Fact]
    public void While_the_total_is_unknown_a_full_page_suggests_more_rows()
    {
        var full = new PageState(pageSize: 500, pageIndex: 0, rowsOnPage: 500, totalRows: null);
        var partial = new PageState(pageSize: 500, pageIndex: 0, rowsOnPage: 12, totalRows: null);

        Assert.True(full.CanGoNext);
        Assert.False(partial.CanGoNext);
        Assert.Null(full.PageCount);
        Assert.False(full.CanGoLast);
    }

    [Fact]
    public void Empty_table()
    {
        var page = new PageState(pageSize: 500, pageIndex: 0, rowsOnPage: 0, totalRows: 0);

        Assert.Equal(0, page.PageCount);
        Assert.Null(page.LastPageIndex);
        Assert.False(page.CanGoNext);
        Assert.False(page.CanGoLast);
    }

    [Fact]
    public void Offsets_do_not_overflow_on_very_large_tables()
    {
        var page = new PageState(pageSize: 500, pageIndex: 10_000_000, rowsOnPage: 500, totalRows: 6_000_000_000);

        Assert.Equal(5_000_000_000, page.Offset);
        Assert.True(page.CanGoNext);
    }
}
