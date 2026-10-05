using System;
using System.Linq;
using System.Threading.Tasks;
using QuickLook.Plugin.DuckDbViewer.Data;
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
    public async Task Clicking_a_header_cycles_ascending_descending_unsorted()
    {
        using var viewModel = new ViewerViewModel();
        await viewModel.OpenAsync(_files.Sales);
        var amount = viewModel.ColumnHeaders.Single(h => h.Name == "amount");
        var region = viewModel.ColumnHeaders.Single(h => h.Name == "region");

        await viewModel.ToggleSortAsync(amount);
        Assert.Equal("1", viewModel.Rows[0][1]);
        Assert.NotEqual(string.Empty, amount.SortGlyph);
        var ascendingGlyph = amount.SortGlyph;

        await viewModel.ToggleSortAsync(amount);
        Assert.Equal("100", viewModel.Rows[0][1]);
        Assert.NotEqual(ascendingGlyph, amount.SortGlyph);

        await viewModel.ToggleSortAsync(region);
        Assert.Equal("east", viewModel.Rows[0][0]);
        Assert.Equal(string.Empty, amount.SortGlyph);

        await viewModel.ToggleSortAsync(region);
        await viewModel.ToggleSortAsync(region);
        Assert.Null(viewModel.Query.Sort);
        Assert.Equal("north", viewModel.Rows[0][0]);
    }

    [Fact]
    public async Task Applying_a_filter_narrows_rows_counts_and_paging()
    {
        using var viewModel = new ViewerViewModel();
        await viewModel.OpenAsync(_files.Sales);
        var region = viewModel.ColumnHeaders.Single(h => h.Name == "region");

        var editor = viewModel.BeginFilter(region);
        await WaitUntil(() => !editor.IsLoading);
        Assert.Equal(4, editor.Items.Count);

        editor.SelectNoneCommand.Execute(null);
        editor.Items.Single(i => i.Value == "north").IsChecked = true;
        await viewModel.ApplyFilterAsync(editor);

        Assert.True(viewModel.HasFilters);
        Assert.True(region.IsFiltered);
        Assert.Equal(3, viewModel.Rows.Count);
        Assert.Equal(3, viewModel.Page.TotalRows);
        Assert.Contains("3", viewModel.SummaryText);
        Assert.Contains("10", viewModel.SummaryText);
        Assert.True(viewModel.ClearFiltersCommand.CanExecute(null));

        // Reopening the editor reflects the applied filter.
        var reopened = viewModel.BeginFilter(region);
        await WaitUntil(() => !reopened.IsLoading);
        Assert.True(reopened.HasExistingFilter);
        Assert.Equal(new[] { "north" }, reopened.Items.Where(i => i.IsChecked).Select(i => i.Value));

        await viewModel.ClearFilterAsync("region");
        Assert.False(viewModel.HasFilters);
        Assert.False(region.IsFiltered);
        Assert.Equal(10, viewModel.Rows.Count);
        Assert.Equal(10, viewModel.Page.TotalRows);
    }

    [Fact]
    public async Task Selecting_another_object_resets_filters_and_sorting()
    {
        using var viewModel = new ViewerViewModel();
        await viewModel.OpenAsync(_files.DuckDb);
        var first = viewModel.SelectedObject;
        await viewModel.ToggleSortAsync(viewModel.ColumnHeaders[0]);
        Assert.NotNull(viewModel.Query.Sort);

        viewModel.SelectedObject = viewModel.Objects.First(o => o != first);
        await WaitUntil(() => !viewModel.IsLoading && viewModel.Page.TotalRows.HasValue);

        Assert.Null(viewModel.Query.Sort);
        Assert.False(viewModel.HasFilters);
    }

    [Fact]
    public async Task Export_writes_the_current_view_next_to_the_file_without_overwriting()
    {
        var path = _files.CopyAs(_files.Sales, "to-export.parquet");
        using var viewModel = new ViewerViewModel();
        await viewModel.OpenAsync(path);
        Assert.True(viewModel.CanExport);

        var editor = viewModel.BeginFilter(viewModel.ColumnHeaders.Single(h => h.Name == "region"));
        await WaitUntil(() => !editor.IsLoading);
        editor.Items.Single(i => i.Value == "south").IsChecked = false;
        await viewModel.ApplyFilterAsync(editor);

        await viewModel.ExportAsync(ExportFormat.Csv);
        var first = viewModel.ExportedPath;
        await viewModel.ExportAsync(ExportFormat.Csv);
        var second = viewModel.ExportedPath;

        Assert.Equal(System.IO.Path.Combine(_files.Directory, "to-export.csv"), first);
        Assert.Equal(System.IO.Path.Combine(_files.Directory, "to-export (2).csv"), second);
        Assert.Equal(8, System.IO.File.ReadAllLines(first!).Length);
        Assert.False(viewModel.IsExporting);
        Assert.True(viewModel.HasStatus);
        Assert.True(viewModel.ShowExportedFileCommand.CanExecute(null));

        viewModel.DismissStatusCommand.Execute(null);
        Assert.False(viewModel.HasStatus);
        Assert.False(viewModel.HasExportedFile);
    }

    [Fact]
    public async Task Export_of_a_database_object_includes_the_object_name()
    {
        var path = _files.CopyAs(_files.DuckDb, "named.duckdb");
        using var viewModel = new ViewerViewModel();
        await viewModel.OpenAsync(path);
        viewModel.SelectedObject = viewModel.Objects.Single(o => o.DisplayName == "other.gamma");
        await WaitUntil(() => viewModel.Page.TotalRows == 1);

        await viewModel.ExportAsync(ExportFormat.Json);

        Assert.Equal(System.IO.Path.Combine(_files.Directory, "named_gamma.json"), viewModel.ExportedPath);
    }

    [Fact]
    public void Export_actions_cover_every_format_and_are_disabled_without_content()
    {
        using var viewModel = new ViewerViewModel();

        Assert.Equal(
            Enum.GetValues(typeof(ExportFormat)).Cast<ExportFormat>(),
            viewModel.ExportActions.Select(a => a.Format));
        Assert.All(viewModel.ExportActions, a => Assert.False(a.Command.CanExecute(null)));
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
