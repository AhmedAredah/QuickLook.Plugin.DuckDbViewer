using System;
using System.Collections.Generic;
using System.Linq;
using DuckDbViewer.Data;
using Xunit;

namespace DuckDbViewer.Tests;

/// <summary>Sorting and filtering, checked against the ten rows of <see cref="SampleFiles.Sales"/>.</summary>
public sealed class RowQueryTests : IClassFixture<SampleFiles>
{
    private const int Region = 0;
    private const int Amount = 1;

    private readonly SampleFiles _files;

    public RowQueryTests(SampleFiles files) => _files = files;

    // ---- Sorting ---------------------------------------------------------------------------

    [Fact]
    public void Numbers_sort_by_value_not_by_text()
    {
        var ascending = Read(Sort("amount"), Amount);
        var descending = Read(Sort("amount", descending: true), Amount);

        Assert.Equal(new[] { "1", "2", "5", "7", "9", "10", "10", "20", "30", "100" }, ascending);
        Assert.Equal(Enumerable.Reverse(ascending), descending);
    }

    [Fact]
    public void Nulls_sort_last_in_both_directions()
    {
        var ascending = Read(Sort("region"), Region);
        var descending = Read(Sort("region", descending: true), Region);

        Assert.Equal(
            new[] { "east", "east", "north", "north", "north", "south", "south", "south", null, null }, ascending);
        Assert.Equal(
            new[] { "south", "south", "south", "north", "north", "north", "east", "east", null, null }, descending);
    }

    [Fact]
    public void Sorting_applies_to_the_whole_table_before_paging()
    {
        using var document = PreviewDocument.Open(_files.Sales);

        var secondPage = document.ReadPage(document.Objects[0], Sort("amount"), offset: 4, limit: 3);

        Assert.Equal(new[] { "9", "10", "10" }, secondPage.Select(r => r[Amount]));
    }

    [Fact]
    public void Sqlite_numeric_columns_sort_numerically_although_read_as_text()
    {
        using var document = PreviewDocument.Open(_files.Sqlite);
        var numbers = document.Objects.Single(o => o.DisplayName == "numbers");
        var column = document.GetColumns(numbers).Single();

        var rows = document.ReadPage(numbers, RowQuery.All.WithSort(new SortOrder(column, false)), 0, 100);

        Assert.Equal(new[] { "2", "9", "10", "100", null }, rows.Select(r => r[0]));
    }

    // ---- Filtering -------------------------------------------------------------------------

    [Theory]
    [InlineData("north", 3)]
    [InlineData("north|south", 6)]
    [InlineData("<null>", 2)]
    [InlineData("north|<null>", 5)]
    [InlineData("nowhere", 0)]
    public void Include_filter_keeps_only_the_listed_values(string values, int expected)
    {
        var query = RowQuery.All.WithFilter(ColumnFilter.Include("region", Parse(values)));

        Assert.Equal(expected, Count(query));
        Assert.Equal(expected, Read(query, Region).Length);
        Assert.All(Read(query, Region), value => Assert.Contains(value, Parse(values)));
    }

    [Theory]
    [InlineData("south", 7)]
    [InlineData("<null>", 8)]
    [InlineData("south|<null>", 5)]
    [InlineData("nowhere", 10)]
    public void Exclude_filter_drops_the_listed_values_and_keeps_nulls_unless_listed(string values, int expected)
    {
        var query = RowQuery.All.WithFilter(ColumnFilter.Exclude("region", Parse(values)));

        Assert.Equal(expected, Count(query));
        Assert.All(Read(query, Region), value => Assert.DoesNotContain(value, Parse(values)));
    }

    [Fact]
    public void Filters_compare_the_displayed_text_of_any_type()
    {
        var query = RowQuery.All.WithFilter(ColumnFilter.Include("amount", ["10"]));

        Assert.Equal(new[] { "south", "south" }, Read(query, Region));
    }

    [Fact]
    public void Filters_on_several_columns_must_all_pass()
    {
        var query = RowQuery.All
            .WithFilter(ColumnFilter.Include("region", ["south"]))
            .WithFilter(ColumnFilter.Exclude("amount", ["2"]))
            .WithSort(Sort("amount").Sort);

        Assert.Equal(new[] { "10", "10" }, Read(query, Amount));
        Assert.Equal(2, Count(query));
    }

    [Fact]
    public void Column_names_and_values_with_special_characters_are_escaped()
    {
        var query = RowQuery.All.WithFilter(ColumnFilter.Include("the note", ["it's"]));

        Assert.Equal(new[] { "south" }, Read(query, Region));
    }

    [Fact]
    public void A_new_filter_on_a_column_replaces_the_previous_one()
    {
        var query = RowQuery.All
            .WithFilter(ColumnFilter.Include("region", ["north"]))
            .WithFilter(ColumnFilter.Include("region", ["east"]));

        Assert.Single(query.Filters);
        Assert.Equal(2, Count(query));
        Assert.False(query.WithoutFilter("region").HasFilters);
        Assert.Same(query.Sort, query.WithoutFilters().Sort);
    }

    // ---- Distinct values -------------------------------------------------------------------

    [Fact]
    public void Distinct_values_are_listed_most_frequent_first_with_counts()
    {
        using var document = PreviewDocument.Open(_files.Sales);

        var values = document.GetDistinctValues(document.Objects[0], "region", RowQuery.All);

        Assert.False(values.IsTruncated);
        Assert.Equal(
            new (string?, long)[] { ("north", 3), ("south", 3), (null, 2), ("east", 2) },
            values.Values.Select(v => (v.Value, v.Count)));
    }

    [Fact]
    public void Distinct_values_respect_other_filters_but_not_the_columns_own()
    {
        using var document = PreviewDocument.Open(_files.Sales);
        var query = RowQuery.All
            .WithFilter(ColumnFilter.Include("region", ["north"]))
            .WithFilter(ColumnFilter.Include("amount", ["10", "5"]));

        var regions = document.GetDistinctValues(document.Objects[0], "region", query);

        // The amount filter narrows the list; the region filter itself does not.
        Assert.Equal(
            new (string?, long)[] { ("south", 2), ("north", 1) },
            regions.Values.Select(v => (v.Value, v.Count)));
    }

    [Fact]
    public void Distinct_values_of_a_high_cardinality_column_are_capped()
    {
        using var document = PreviewDocument.Open(_files.Parquet);

        var ids = document.GetDistinctValues(document.Objects[0], "id", RowQuery.All);

        Assert.True(ids.IsTruncated);
        Assert.Equal(TableReader.MaxDistinctValues, ids.Values.Count);
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private RowQuery Sort(string column, bool descending = false)
    {
        using var document = PreviewDocument.Open(_files.Sales);
        var info = document.GetColumns(document.Objects[0]).Single(c => c.Name == column);
        return RowQuery.All.WithSort(new SortOrder(info, descending));
    }

    private string?[] Read(RowQuery query, int column)
    {
        using var document = PreviewDocument.Open(_files.Sales);
        return document.ReadPage(document.Objects[0], query, 0, 100).Select(r => r[column]).ToArray();
    }

    private long Count(RowQuery query)
    {
        using var document = PreviewDocument.Open(_files.Sales);
        return document.CountRows(document.Objects[0], query);
    }

    private static List<string?> Parse(string values)
    {
        return values.Split('|').Select(v => v == "<null>" ? null : v).ToList();
    }
}
