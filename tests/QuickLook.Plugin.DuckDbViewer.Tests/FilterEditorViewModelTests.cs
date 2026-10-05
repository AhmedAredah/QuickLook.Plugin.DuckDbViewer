using System.Linq;
using QuickLook.Plugin.DuckDbViewer.Data;
using QuickLook.Plugin.DuckDbViewer.ViewModels;
using Xunit;

namespace QuickLook.Plugin.DuckDbViewer.Tests;

public sealed class FilterEditorViewModelTests
{
    private static readonly ColumnInfo Column = new("region", "VARCHAR", true);

    private static ColumnValues Values(bool truncated, params string?[] values)
    {
        return new ColumnValues(values.Select(v => new ValueCount(v, 1)).ToList(), truncated);
    }

    private static FilterValueItem Item(FilterEditorViewModel editor, string? value)
    {
        return editor.Items.Single(i => i.Value == value);
    }

    [Fact]
    public void A_new_filter_starts_with_everything_ticked_and_means_no_filter()
    {
        var editor = new FilterEditorViewModel(Column, existing: null);
        Assert.True(editor.IsLoading);
        Assert.False(editor.CanApply);

        editor.Load(Values(false, "north", "south", null));

        Assert.False(editor.IsLoading);
        Assert.All(editor.Items, item => Assert.True(item.IsChecked));
        Assert.True(editor.CanApply);
        Assert.Null(editor.BuildFilter());
    }

    [Fact]
    public void Unticking_values_excludes_them()
    {
        var editor = new FilterEditorViewModel(Column, null);
        editor.Load(Values(true, "north", "south", null));

        Item(editor, "south").IsChecked = false;
        var filter = editor.BuildFilter()!;

        // Exclusion keeps values that were never listed because the list was truncated.
        Assert.True(filter.IsExclusion);
        Assert.Equal(new[] { "south" }, filter.Values);
        Assert.True(filter.Accepts("a value that was not listed"));
    }

    [Fact]
    public void Select_none_then_ticking_values_includes_only_them()
    {
        var editor = new FilterEditorViewModel(Column, null);
        editor.Load(Values(true, "north", "south", null));

        editor.SelectNoneCommand.Execute(null);
        Assert.False(editor.CanApply);

        Item(editor, null).IsChecked = true;
        var filter = editor.BuildFilter()!;

        Assert.True(editor.CanApply);
        Assert.False(filter.IsExclusion);
        Assert.Equal(new string?[] { null }, filter.Values);
        Assert.False(filter.Accepts("a value that was not listed"));
    }

    [Fact]
    public void Select_all_after_select_none_means_no_filter_again()
    {
        var editor = new FilterEditorViewModel(Column, null);
        editor.Load(Values(false, "north", "south"));

        editor.SelectNoneCommand.Execute(null);
        editor.SelectAllCommand.Execute(null);

        Assert.Null(editor.BuildFilter());
    }

    [Fact]
    public void Reopening_shows_the_current_filter_and_keeps_values_missing_from_the_list()
    {
        var existing = ColumnFilter.Include("region", ["north", "rare"]);
        var editor = new FilterEditorViewModel(Column, existing);
        Assert.True(editor.HasExistingFilter);

        editor.Load(Values(false, "north", "south"));

        Assert.True(Item(editor, "north").IsChecked);
        Assert.False(Item(editor, "south").IsChecked);
        Assert.True(Item(editor, "rare").IsChecked);

        var rebuilt = editor.BuildFilter()!;
        Assert.False(rebuilt.IsExclusion);
        Assert.Equal(new[] { "north", "rare" }, rebuilt.Values.OrderBy(v => v));
    }

    [Fact]
    public void A_failure_to_list_values_blocks_applying()
    {
        var editor = new FilterEditorViewModel(Column, null);

        editor.Fail("boom");

        Assert.True(editor.HasError);
        Assert.False(editor.IsLoading);
        Assert.False(editor.CanApply);
    }

    [Fact]
    public void Long_and_multiline_values_are_shortened_for_display_only()
    {
        var value = "line one\nline two " + new string('x', 200);
        var editor = new FilterEditorViewModel(Column, null);
        editor.Load(Values(false, value, null));

        var item = Item(editor, value);

        Assert.DoesNotContain("\n", item.Display);
        Assert.EndsWith("…", item.Display);
        Assert.Equal("NULL", Item(editor, null).Display);
        Assert.True(Item(editor, null).IsNull);
    }
}
