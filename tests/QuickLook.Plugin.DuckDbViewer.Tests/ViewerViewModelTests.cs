using System;
using System.Linq;
using System.Threading.Tasks;
using QuickLook.Plugin.DuckDbViewer.ViewModels;
using Xunit;

namespace QuickLook.Plugin.DuckDbViewer.Tests;

public sealed class ViewerViewModelTests : IClassFixture<SampleFiles>
{
    private readonly SampleFiles _files;

    public ViewerViewModelTests(SampleFiles files) => _files = files;

    [Fact]
    public async Task Opening_a_parquet_file_shows_the_first_page_and_the_total()
    {
        using var viewModel = new ViewerViewModel();
        var ready = 0;
        viewModel.ContentReady += (_, _) => ready++;

        await viewModel.OpenAsync(_files.Parquet);

        Assert.Equal(1, ready);
        Assert.True(viewModel.HasContent);
        Assert.False(viewModel.HasObjectList);
        Assert.False(viewModel.IsLoading);
        Assert.Equal("Parquet", viewModel.FormatName);
        Assert.Equal(7, viewModel.Columns.Count);
        Assert.Equal(7, viewModel.SchemaRows.Count);
        Assert.Equal(ViewerViewModel.PageSize, viewModel.Rows.Count);
        Assert.Equal(SampleFiles.ParquetRowCount, viewModel.Page.TotalRows);
        Assert.True(viewModel.NextPageCommand.CanExecute(null));
        Assert.False(viewModel.PreviousPageCommand.CanExecute(null));
    }

    [Fact]
    public async Task Paging_moves_through_the_table()
    {
        using var viewModel = new ViewerViewModel();
        await viewModel.OpenAsync(_files.Parquet);

        await viewModel.GoToPageAsync(viewModel.Page.LastPageIndex!.Value);

        Assert.Equal(2, viewModel.Page.PageIndex);
        Assert.Equal(SampleFiles.ParquetRowCount - 2 * ViewerViewModel.PageSize, viewModel.Rows.Count);
        Assert.Equal("1000", viewModel.Rows[0][0]);
        Assert.False(viewModel.NextPageCommand.CanExecute(null));
        Assert.True(viewModel.FirstPageCommand.CanExecute(null));
    }

    [Fact]
    public async Task Selecting_another_object_replaces_schema_and_rows()
    {
        using var viewModel = new ViewerViewModel();
        await viewModel.OpenAsync(_files.DuckDb);
        Assert.True(viewModel.HasObjectList);

        viewModel.SelectedObject = viewModel.Objects.Single(o => o.DisplayName == "other.gamma");
        await WaitUntil(() => viewModel.Page.TotalRows == 1);

        Assert.Equal("answer", Assert.Single(viewModel.Columns).Name);
        Assert.Equal("42", viewModel.Rows[0][0]);
        Assert.False(viewModel.HasNotice);
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_shows_a_notice_instead_of_content()
    {
        var path = _files.WriteText("broken.parquet", "PAR1 followed by nothing that resembles parquet");
        using var viewModel = new ViewerViewModel();
        var ready = 0;
        viewModel.ContentReady += (_, _) => ready++;

        await viewModel.OpenAsync(path);

        Assert.Equal(1, ready);
        Assert.True(viewModel.HasNotice);
        Assert.False(viewModel.HasContent);
        Assert.False(viewModel.IsLoading);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.NoticeDetail));
        Assert.DoesNotContain("LINE 1", viewModel.NoticeDetail);
    }

    [Fact]
    public async Task Disposing_releases_the_file()
    {
        var path = _files.CopyAs(_files.DuckDb, "to-release.duckdb");
        var viewModel = new ViewerViewModel();
        await viewModel.OpenAsync(path);

        viewModel.Dispose();

        await WaitUntil(() =>
        {
            try
            {
                System.IO.File.Delete(path);
                return true;
            }
            catch (System.IO.IOException)
            {
                return false;
            }
        });
    }

    [Fact]
    public void Error_descriptions_omit_the_generated_statement()
    {
        var error = new InvalidOperationException(
            "Catalog Error: Table with name alpha does not exist!\nDid you mean \"main.alpha\"?\n\n" +
            "LINE 1: DESCRIBE SELECT * FROM \"preview\".\"main\".\"v\"\n                ^");

        Assert.Equal(
            "Catalog Error: Table with name alpha does not exist!\nDid you mean \"main.alpha\"?",
            ViewerViewModel.DescribeError(error));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the view model.");
            await Task.Delay(20);
        }
    }
}
