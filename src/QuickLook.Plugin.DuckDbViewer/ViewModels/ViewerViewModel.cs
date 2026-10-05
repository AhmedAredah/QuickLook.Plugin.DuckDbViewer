using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickLook.Plugin.DuckDbViewer.Data;
using QuickLook.Plugin.DuckDbViewer.Detection;
using QuickLook.Plugin.DuckDbViewer.Localization;

namespace QuickLook.Plugin.DuckDbViewer.ViewModels;

/// <summary>
/// State and behaviour of the preview panel. All file access runs on background threads;
/// results that arrive after the user moved on (another object, another page, the preview
/// closed) are cancelled and discarded.
/// </summary>
internal sealed class ViewerViewModel : ObservableObject, IDisposable
{
    public const int PageSize = 500;

    private static readonly IReadOnlyList<ColumnInfo> NoColumns = [];
    private static readonly IReadOnlyList<string?[]> NoRows = [];

    // Three nested cancellation scopes: the whole preview, the selected object, the page request.
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _objectScope;
    private CancellationTokenSource? _pageScope;

    private PreviewDocument? _document;
    private bool _contentReadyRaised;

    private IReadOnlyList<DataObject> _objects = [];
    private DataObject? _selectedObject;
    private IReadOnlyList<ColumnInfo> _columns = NoColumns;
    private IReadOnlyList<SchemaRow> _schemaRows = [];
    private IReadOnlyList<string?[]> _rows = NoRows;
    private PageState _page = new(PageSize, 0, 0, null);
    private bool _isLoading;
    private bool _isSchemaVisible;
    private string _formatName = string.Empty;
    private string? _noticeTitle;
    private string? _noticeDetail;

    public ViewerViewModel()
    {
        FirstPageCommand = new DelegateCommand(() => _ = GoToPageAsync(0), () => _page.CanGoPrevious);
        PreviousPageCommand = new DelegateCommand(() => _ = GoToPageAsync(_page.PageIndex - 1), () => _page.CanGoPrevious);
        NextPageCommand = new DelegateCommand(() => _ = GoToPageAsync(_page.PageIndex + 1), () => _page.CanGoNext);
        LastPageCommand = new DelegateCommand(() => _ = GoToPageAsync(_page.LastPageIndex ?? 0), () => _page.CanGoLast);
    }

    /// <summary>Raised once, when there is something to show (content or a notice).</summary>
    public event EventHandler? ContentReady;

    public DelegateCommand FirstPageCommand { get; }

    public DelegateCommand PreviousPageCommand { get; }

    public DelegateCommand NextPageCommand { get; }

    public DelegateCommand LastPageCommand { get; }

    public IReadOnlyList<DataObject> Objects
    {
        get => _objects;
        private set
        {
            if (Set(ref _objects, value))
                OnPropertyChanged(nameof(HasObjectList));
        }
    }

    /// <summary>A single-table file (Parquet) needs no object list.</summary>
    public bool HasObjectList => _objects.Count > 1 || _objects.Any(o => o.IsView);

    public DataObject? SelectedObject
    {
        get => _selectedObject;
        set
        {
            if (Set(ref _selectedObject, value) && value is not null)
                _ = LoadObjectAsync(value);
        }
    }

    public IReadOnlyList<ColumnInfo> Columns
    {
        get => _columns;
        private set
        {
            if (!Set(ref _columns, value))
                return;

            SchemaRows = value.Select((column, index) => new SchemaRow(index + 1, column)).ToList();
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public IReadOnlyList<SchemaRow> SchemaRows
    {
        get => _schemaRows;
        private set => Set(ref _schemaRows, value);
    }

    public IReadOnlyList<string?[]> Rows
    {
        get => _rows;
        private set => Set(ref _rows, value);
    }

    public PageState Page
    {
        get => _page;
        private set
        {
            _page = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(RangeText));
            OnPropertyChanged(nameof(PageText));
            FirstPageCommand.RaiseCanExecuteChanged();
            PreviousPageCommand.RaiseCanExecuteChanged();
            NextPageCommand.RaiseCanExecuteChanged();
            LastPageCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (Set(ref _isLoading, value))
                OnPropertyChanged(nameof(RangeText));
        }
    }

    public bool IsSchemaVisible
    {
        get => _isSchemaVisible;
        set
        {
            if (!Set(ref _isSchemaVisible, value))
                return;

            OnPropertyChanged(nameof(IsDataVisible));
            OnContentVisibilityChanged();
        }
    }

    public bool IsDataVisible
    {
        get => !_isSchemaVisible;
        set => IsSchemaVisible = !value;
    }

    /// <summary>Whether a file is open and has at least one object to browse.</summary>
    public bool HasDocument => _document is not null && _objects.Count > 0;

    /// <summary>Whether the selected object is shown; false while a notice takes its place.</summary>
    public bool HasContent => HasDocument && !HasNotice;

    public bool ShowDataGrid => HasContent && !_isSchemaVisible;

    public bool ShowSchemaGrid => HasContent && _isSchemaVisible;

    public string FormatName
    {
        get => _formatName;
        private set => Set(ref _formatName, value);
    }

    /// <summary>A message shown instead of the grid: a failure or an empty database.</summary>
    public string? NoticeTitle
    {
        get => _noticeTitle;
        private set
        {
            if (!Set(ref _noticeTitle, value))
                return;

            OnPropertyChanged(nameof(HasNotice));
            OnContentVisibilityChanged();
        }
    }

    public string? NoticeDetail
    {
        get => _noticeDetail;
        private set => Set(ref _noticeDetail, value);
    }

    public bool HasNotice => _noticeTitle is not null;

    public string SummaryText => _page.TotalRows is { } total
        ? Strings.Format("Summary_RowsAndColumns", total.ToString("N0"), _columns.Count.ToString("N0"))
        : Strings.Format("Summary_Columns", _columns.Count.ToString("N0"));

    public string RangeText
    {
        get
        {
            if (_page.RowsOnPage == 0)
                return _isLoading ? string.Empty : Strings.Get("Range_Empty");

            var first = _page.FirstRow.ToString("N0");
            var last = _page.LastRow.ToString("N0");
            return _page.TotalRows is { } total
                ? Strings.Format("Range_Known", first, last, total.ToString("N0"))
                : Strings.Format("Range_Unknown", first, last);
        }
    }

    public string PageText
    {
        get
        {
            var current = (_page.PageIndex + 1).ToString("N0");
            return _page.PageCount is { } count && count > 0
                ? Strings.Format("Pager_PageOf", current, count.ToString("N0"))
                : Strings.Format("Pager_Page", current);
        }
    }

    /// <summary>Opens the file and shows its first object.</summary>
    public async Task OpenAsync(string path)
    {
        var token = _lifetime.Token;
        IsLoading = true;

        try
        {
            var document = await Task.Run(() => PreviewDocument.Open(path, token), token);
            if (token.IsCancellationRequested)
            {
                document.Dispose();
                return;
            }

            _document = document;
            FormatName = DescribeFormat(document.Format);
            Objects = document.Objects;
            OnPropertyChanged(nameof(HasDocument));
            OnContentVisibilityChanged();

            if (document.Objects.Count == 0)
            {
                ShowNotice(Strings.Get("Notice_NoObjects"), null);
                return;
            }

            // Assign the field directly so the load can be awaited here.
            _selectedObject = document.Objects[0];
            OnPropertyChanged(nameof(SelectedObject));
            await LoadObjectAsync(document.Objects[0]);
        }
        catch (OperationCanceledException)
        {
            // The preview was closed while opening.
        }
        catch (Exception e)
        {
            ShowNotice(Strings.Get("Notice_OpenFailed"), DescribeError(e));
        }
    }

    private async Task LoadObjectAsync(DataObject source)
    {
        if (_document is not { } document)
            return;

        var token = BeginScope(ref _objectScope, _lifetime.Token);
        CancelScope(ref _pageScope);

        NoticeTitle = null;
        NoticeDetail = null;
        Rows = NoRows;
        Columns = NoColumns;
        IsLoading = true;
        Page = new PageState(PageSize, 0, 0, null);

        try
        {
            var (columns, rows) = await Task.Run(
                () => (document.GetColumns(source, token), document.ReadPage(source, 0, PageSize, token)),
                token);
            if (token.IsCancellationRequested)
                return;

            Columns = columns;
            Rows = rows;
            IsLoading = false;
            Page = new PageState(PageSize, 0, rows.Count, null);
            RaiseContentReady();

            // Counting can take a while on large views; the first page is already visible.
            var total = await Task.Run(() => document.CountRows(source, token), token);
            if (token.IsCancellationRequested)
                return;

            Page = new PageState(PageSize, _page.PageIndex, _page.RowsOnPage, total);
        }
        catch (OperationCanceledException)
        {
            // Superseded by another selection or the preview was closed.
        }
        catch (Exception e) when (!token.IsCancellationRequested)
        {
            ShowReadFailure(e);
        }
    }

    public async Task GoToPageAsync(int pageIndex)
    {
        if (_document is not { } document || _selectedObject is not { } source || _objectScope is null)
            return;

        var token = BeginScope(ref _pageScope, _objectScope.Token);
        IsLoading = true;

        try
        {
            var rows = await Task.Run(
                () => document.ReadPage(source, (long)pageIndex * PageSize, PageSize, token),
                token);
            if (token.IsCancellationRequested)
                return;

            Rows = rows;
            IsLoading = false;
            Page = new PageState(PageSize, pageIndex, rows.Count, _page.TotalRows);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer page request.
        }
        catch (Exception e) when (!token.IsCancellationRequested)
        {
            ShowReadFailure(e);
        }
    }

    /// <summary>
    /// A file that is a single table (Parquet) has nothing else to offer when that table
    /// cannot be read, so it is reported as a failure of the file itself.
    /// </summary>
    private void ShowReadFailure(Exception error)
    {
        var title = HasObjectList ? "Notice_ReadFailed" : "Notice_OpenFailed";
        ShowNotice(Strings.Get(title), DescribeError(error));
    }

    /// <summary>
    /// DuckDB appends the failing statement to its messages ("LINE 1: SELECT ..."); that part
    /// is the plugin's own SQL and means nothing to the user.
    /// </summary>
    internal static string DescribeError(Exception error)
    {
        var message = error.Message.Replace("\r\n", "\n");
        var statement = message.IndexOf("\nLINE ", StringComparison.Ordinal);
        if (statement >= 0)
            message = message.Substring(0, statement);

        return message.Trim();
    }

    private void OnContentVisibilityChanged()
    {
        OnPropertyChanged(nameof(HasContent));
        OnPropertyChanged(nameof(ShowDataGrid));
        OnPropertyChanged(nameof(ShowSchemaGrid));
    }

    private void ShowNotice(string title, string? detail)
    {
        IsLoading = false;
        NoticeDetail = detail;
        NoticeTitle = title;
        RaiseContentReady();
    }

    private void RaiseContentReady()
    {
        if (_contentReadyRaised)
            return;

        _contentReadyRaised = true;
        ContentReady?.Invoke(this, EventArgs.Empty);
    }

    private static string DescribeFormat(FileFormat format) => format switch
    {
        FileFormat.Parquet => "Parquet",
        FileFormat.DuckDb => "DuckDB",
        FileFormat.Sqlite => "SQLite",
        _ => string.Empty,
    };

    /// <summary>Cancels whatever ran in <paramref name="scope"/> and starts a fresh one.</summary>
    private static CancellationToken BeginScope(ref CancellationTokenSource? scope, CancellationToken parent)
    {
        CancelScope(ref scope);
        scope = CancellationTokenSource.CreateLinkedTokenSource(parent);
        return scope.Token;
    }

    private static void CancelScope(ref CancellationTokenSource? scope)
    {
        scope?.Cancel();
        scope?.Dispose();
        scope = null;
    }

    public void Dispose()
    {
        if (_lifetime.IsCancellationRequested)
            return;

        // Cancelling interrupts a running query; closing the engine then releases the file.
        // Done off the UI thread because it waits for that query to stop.
        _lifetime.Cancel();
        var document = _document;
        _document = null;
        if (document is not null)
            Task.Run(document.Dispose);
    }
}
