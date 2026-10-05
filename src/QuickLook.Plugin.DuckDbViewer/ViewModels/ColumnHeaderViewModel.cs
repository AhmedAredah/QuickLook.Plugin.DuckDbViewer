using QuickLook.Plugin.DuckDbViewer.Data;

namespace QuickLook.Plugin.DuckDbViewer.ViewModels;

/// <summary>What a column header of the data grid shows: name, type, sort arrow, filter mark.</summary>
internal sealed class ColumnHeaderViewModel : ObservableObject
{
    private const string AscendingGlyph = "";
    private const string DescendingGlyph = "";

    private bool _isFiltered;
    private string _sortGlyph = string.Empty;

    public ColumnHeaderViewModel(ColumnInfo column) => Column = column;

    public ColumnInfo Column { get; }

    public string Name => Column.Name;

    public string Type => Column.Type;

    public bool IsFiltered
    {
        get => _isFiltered;
        private set => Set(ref _isFiltered, value);
    }

    /// <summary>An arrow from the icon font, or empty when the grid is not sorted by this column.</summary>
    public string SortGlyph
    {
        get => _sortGlyph;
        private set => Set(ref _sortGlyph, value);
    }

    /// <summary>Updates the marks to reflect <paramref name="query"/>.</summary>
    public void Reflect(RowQuery query)
    {
        IsFiltered = query.FilterOn(Name) is not null;
        SortGlyph = query.Sort is { } sort && sort.Column.Name == Name
            ? sort.Descending ? DescendingGlyph : AscendingGlyph
            : string.Empty;
    }
}
