namespace DuckDbViewer.ViewModels;

/// <summary>
/// Paging arithmetic. The total row count arrives later than the first page (counting can
/// be slow), so every answer must also make sense while the total is still unknown.
/// </summary>
internal readonly struct PageState
{
    public PageState(int pageSize, int pageIndex, int rowsOnPage, long? totalRows)
    {
        PageSize = pageSize;
        PageIndex = pageIndex;
        RowsOnPage = rowsOnPage;
        TotalRows = totalRows;
    }

    public int PageSize { get; }

    /// <summary>Zero-based.</summary>
    public int PageIndex { get; }

    public int RowsOnPage { get; }

    public long? TotalRows { get; }

    /// <summary>Zero-based position of the page's first row within the whole object.</summary>
    public long Offset => (long)PageIndex * PageSize;

    public long FirstRow => Offset + 1;

    public long LastRow => Offset + RowsOnPage;

    public int? PageCount => TotalRows is { } total
        ? (int)((total + PageSize - 1) / PageSize)
        : null;

    public int? LastPageIndex => PageCount is { } count && count > 0 ? count - 1 : null;

    public bool CanGoPrevious => PageIndex > 0;

    /// <summary>Without a total, a full page is taken as a sign that more rows follow.</summary>
    public bool CanGoNext => TotalRows is { } total
        ? Offset + PageSize < total
        : RowsOnPage == PageSize;

    public bool CanGoLast => LastPageIndex is { } last && PageIndex < last;
}
